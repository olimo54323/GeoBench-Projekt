using System.Data.Common;
using System.Text;
using GeoBench.Core.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace GeoBench.Core.Storage
{
    /// <summary>Magazyn SQL Server: geometria w formacie natywnym (SqlServerBytes), plan z SET STATISTICS XML.</summary>
    public sealed class SqlServerSpatialStore : RelationalSpatialStore
    {
        private const string ServerInfoSql = """
            SELECT N'edycja' AS nazwa, CAST(SERVERPROPERTY('Edition') AS nvarchar(4000)) AS wartosc
            UNION ALL SELECT N'wersja', CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(4000))
            UNION ALL SELECT CAST(name AS nvarchar(4000)), CAST(value_in_use AS nvarchar(4000))
                FROM sys.configurations
                WHERE name IN (N'max degree of parallelism', N'cost threshold for parallelism', N'max server memory (MB)')
            UNION ALL SELECT CAST(N'indeks ' + OBJECT_NAME(t.object_id) + N'.' + i.name AS nvarchar(4000)),
                CONCAT(t.tessellation_scheme, N' ', t.level_1_grid_desc, N'/', t.level_2_grid_desc, N'/', t.level_3_grid_desc, N'/',
                       t.level_4_grid_desc, N', cells_per_object=', t.cells_per_object)
                FROM sys.spatial_index_tessellations AS t
                JOIN sys.indexes AS i ON i.object_id = t.object_id AND i.index_id = t.index_id
            """;

        private readonly SqlServerBytesReader _geometryReader = new() { IsGeography = false };

        public SqlServerSpatialStore(SqlServerContext context) : base(context)
        {
        }

        public override DatabaseEngine Engine => DatabaseEngine.SqlServer;

        protected override string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

        protected override string ExtentSql(string quotedTable, string quotedColumn) =>
            $"SELECT geometry::EnvelopeAggregate({quotedColumn}) FROM {quotedTable}";

        protected override string InvalidCondition(string quotedColumn) => $"{quotedColumn}.STIsValid() = 0";

        // Strony w wierszu, LOB i przepełnienia tabeli (indeks przestrzenny ma własną tabelę wewnętrzną, więc się nie liczy)
        protected override string TableSizeSql(SpatialLayer layer) =>
            $"SELECT SUM(used_page_count) * 8192 FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID({Literal(layer.Table)}) AND index_id IN (0, 1)";

        public override async Task<SpatialIndexInfo?> SpatialIndexAsync(SpatialLayer layer, CancellationToken cancellationToken = default)
        {
            // Rozmiar i liczba wierszy (komórek siatki) z wewnętrznej tabeli indeksu przestrzennego
            var row = await ReadRowAsync($"""
                SELECT TOP (1) i.name AS [index], t.tessellation_scheme,
                    t.bounding_box_xmin AS xmin, t.bounding_box_ymin AS ymin, t.bounding_box_xmax AS xmax, t.bounding_box_ymax AS ymax,
                    t.level_1_grid_desc AS level_1, t.level_2_grid_desc AS level_2, t.level_3_grid_desc AS level_3,
                    t.level_4_grid_desc AS level_4, t.cells_per_object,
                    (SELECT SUM(ps.used_page_count) * 8192 FROM sys.internal_tables AS it
                        JOIN sys.dm_db_partition_stats AS ps ON ps.object_id = it.object_id
                        WHERE it.parent_object_id = i.object_id AND it.parent_minor_id = i.index_id) AS size_bytes,
                    (SELECT SUM(ps.row_count) FROM sys.internal_tables AS it
                        JOIN sys.dm_db_partition_stats AS ps ON ps.object_id = it.object_id AND ps.index_id IN (0, 1)
                        WHERE it.parent_object_id = i.object_id AND it.parent_minor_id = i.index_id) AS row_count
                FROM sys.spatial_indexes AS i
                JOIN sys.spatial_index_tessellations AS t ON t.object_id = i.object_id AND t.index_id = i.index_id
                JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID({Literal(layer.Table)}) AND c.name = {Literal(layer.GeometryColumn)}
                ORDER BY i.index_id
                """, cancellationToken);
            if (row == null)
                return null;

            var parameters = row.Where(p => p.Key is not ("size_bytes" or "row_count"))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            parameters["table"] = layer.Table;
            parameters["column"] = layer.GeometryColumn;
            return new SpatialIndexInfo(row["index"], parameters,
                long.TryParse(row["size_bytes"], out var size) ? size : 0,
                long.TryParse(row["row_count"], out var rows) ? rows : null);
        }

        protected override Geometry ReadGeometry(DbDataReader reader, int ordinal) =>
            // Kolumna geometry to typ UDT: bez Microsoft.SqlServer.Types czytamy surowe bajty
            _geometryReader.Read(((SqlDataReader)reader).GetSqlBytes(ordinal).Value);

        public override Task<IReadOnlyList<KeyValuePair<string, string>>> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
            ReadSettingsAsync(ServerInfoSql, cancellationToken);

        public override async Task<QueryDiagnostics> DiagnoseAsync(string sql, CancellationToken cancellationToken = default)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
            var connection = (SqlConnection)Context.Database.GetDbConnection();
            var messages = new StringBuilder();
            void OnInfoMessage(object sender, SqlInfoMessageEventArgs e) => messages.AppendLine(e.Message);
            connection.InfoMessage += OnInfoMessage;
            try
            {
                // Jedna paczka: ustawienia sesji wracają do OFF także przy połączeniu z puli
                await using var command = CreateCommand(
                    "SET STATISTICS TIME ON; SET STATISTICS IO ON; SET STATISTICS XML ON;\n" + sql +
                    "\n;\nSET STATISTICS XML OFF; SET STATISTICS IO OFF; SET STATISTICS TIME OFF;");
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var plan = "";
                do
                {
                    // Plan to osobny zbiór wyników z jedną kolumną XML Showplan
                    var isPlan = reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        if (isPlan)
                            plan = reader.GetString(0);
                    }
                } while (await reader.NextResultAsync(cancellationToken));
                return new QueryDiagnostics("xml", plan, messages.ToString());
            }
            finally
            {
                connection.InfoMessage -= OnInfoMessage;
                await Context.Database.CloseConnectionAsync();
            }
        }
    }
}
