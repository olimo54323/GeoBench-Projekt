using System.Data.Common;
using GeoBench.Core.Data;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Storage
{
    /// <summary>Magazyn PostgreSQL: geometria jako typ NTS z pluginu Npgsql, plan z EXPLAIN (ANALYZE, BUFFERS).</summary>
    public sealed class PostgresSpatialStore : RelationalSpatialStore
    {
        private const string ServerInfoSql = """
            SELECT 'wersja' AS nazwa, current_setting('server_version') AS wartosc
            UNION ALL SELECT 'postgis', extversion FROM pg_extension WHERE extname = 'postgis'
            UNION ALL SELECT name, setting || COALESCE(' ' || unit, '')
                FROM pg_settings
                WHERE name IN ('shared_buffers', 'work_mem', 'maintenance_work_mem', 'effective_cache_size', 'max_parallel_workers_per_gather',
                               'max_parallel_workers', 'random_page_cost', 'jit')
            UNION ALL SELECT 'indeks ' || tablename || '.' || indexname, indexdef
                FROM pg_indexes
                WHERE schemaname = current_schema() AND indexdef ILIKE '%USING gist%'
            """;

        public PostgresSpatialStore(PostgresContext context) : base(context)
        {
        }

        public override DatabaseEngine Engine => DatabaseEngine.Postgres;

        protected override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

        protected override string ExtentSql(string quotedTable, string quotedColumn) =>
            $"SELECT ST_SetSRID(ST_Extent({quotedColumn})::geometry, {GeoBenchContext.Srid}) FROM {quotedTable}";

        protected override string InvalidCondition(string quotedColumn) => $"NOT ST_IsValid({quotedColumn})";

        // Tabela z TOAST (duże geometrie), bez indeksów
        protected override string TableSizeSql(SpatialLayer layer) =>
            $"SELECT pg_table_size(to_regclass(quote_ident(current_schema()) || '.' || quote_ident({Literal(layer.Table)})))";

        public override async Task<SpatialIndexInfo?> SpatialIndexAsync(SpatialLayer layer, CancellationToken cancellationToken = default)
        {
            // Indeks GiST na kolumnie geometrii; reltuples = -1, gdy tabeli jeszcze nie analizowano
            var row = await ReadRowAsync($"""
                SELECT i.relname AS index, n.nspname AS schema, pg_get_indexdef(i.oid) AS definition,
                       pg_relation_size(i.oid) AS size_bytes, i.reltuples::bigint AS row_count
                FROM pg_index AS x
                JOIN pg_class AS i ON i.oid = x.indexrelid
                JOIN pg_class AS t ON t.oid = x.indrelid
                JOIN pg_namespace AS n ON n.oid = t.relnamespace
                JOIN pg_am AS am ON am.oid = i.relam
                JOIN pg_attribute AS a ON a.attrelid = t.oid AND a.attnum = ANY (x.indkey)
                WHERE n.nspname = current_schema() AND t.relname = {Literal(layer.Table)}
                  AND a.attname = {Literal(layer.GeometryColumn)} AND am.amname = 'gist'
                ORDER BY i.relname
                LIMIT 1
                """, cancellationToken);
            if (row == null)
                return null;

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["index"] = row["index"],
                ["schema"] = row["schema"],
                ["definition"] = row["definition"],
                ["table"] = layer.Table,
                ["column"] = layer.GeometryColumn
            };
            return new SpatialIndexInfo(row["index"], parameters,
                long.TryParse(row["size_bytes"], out var size) ? size : 0,
                long.TryParse(row["row_count"], out var rows) && rows >= 0 ? rows : null);
        }

        // Połączenie kontekstu ma plugin NetTopologySuite (UseNetTopologySuite), więc sterownik zwraca typ NTS
        protected override Geometry ReadGeometry(DbDataReader reader, int ordinal) => reader.GetFieldValue<Geometry>(ordinal);

        public override Task<IReadOnlyList<KeyValuePair<string, string>>> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
            ReadSettingsAsync(ServerInfoSql, cancellationToken);

        public override async Task<QueryDiagnostics> DiagnoseAsync(string sql, CancellationToken cancellationToken = default)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                // EXPLAIN ANALYZE wykonuje zapytanie naprawdę; czasy planowania i wykonania są w JSON
                await using var command = CreateCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)\n" + sql);
                var plan = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "";
                return new QueryDiagnostics("json", plan, "");
            }
            finally
            {
                await Context.Database.CloseConnectionAsync();
            }
        }
    }
}
