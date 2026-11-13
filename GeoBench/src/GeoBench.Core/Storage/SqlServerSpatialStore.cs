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

        protected override string InvalidCondition(string quotedColumn) => $"{quotedColumn}.STIsValid() = 0";

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
