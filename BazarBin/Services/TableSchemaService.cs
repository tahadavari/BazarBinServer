using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BazarBin.Services;

public sealed class TableSchemaService
{
    private readonly string _connectionString;
    private readonly ILogger<TableSchemaService> _logger;

    public TableSchemaService(IConfiguration configuration, ILogger<TableSchemaService> logger)
    {
        _connectionString = configuration.GetConnectionString("ImportDatabase")
                           ?? throw new InvalidOperationException("Connection string 'ImportDatabase' is not configured.");
        _logger = logger;
    }

    public async Task<TableSchemaResult> GetTableSchemaAsync(string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var tableMetadata = await LoadTableMetadataAsync(connection, schemaName, tableName, cancellationToken);
            if (tableMetadata is null)
            {
                throw new InvalidOperationException($"Table '{schemaName}.{tableName}' was not found.");
            }

            var columns = await LoadColumnMetadataAsync(connection, schemaName, tableName, cancellationToken);
            return new TableSchemaResult(tableMetadata, columns);
        }
        catch (PostgresException ex)
        {
            _logger.LogWarning(ex, "PostgreSQL error while loading table schema for {Schema}.{Table}.", schemaName, tableName);
            throw;
        }
    }

    private static async Task<TableMetadata?> LoadTableMetadataAsync(NpgsqlConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(TableMetadataSql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TableMetadata(
            Schema: reader.GetString(reader.GetOrdinal("table_schema")),
            Name: reader.GetString(reader.GetOrdinal("table_name")),
            Type: reader.GetString(reader.GetOrdinal("table_type")),
            Owner: reader.GetString(reader.GetOrdinal("table_owner")),
            Comment: reader.IsDBNull(reader.GetOrdinal("table_comment")) ? null : reader.GetString(reader.GetOrdinal("table_comment")),
            ApproximateRowCount: reader.IsDBNull(reader.GetOrdinal("approximate_row_count")) ? null : reader.GetInt64(reader.GetOrdinal("approximate_row_count")),
            TotalBytes: reader.IsDBNull(reader.GetOrdinal("total_relation_size_bytes")) ? null : reader.GetInt64(reader.GetOrdinal("total_relation_size_bytes")),
            TableBytes: reader.IsDBNull(reader.GetOrdinal("table_size_bytes")) ? null : reader.GetInt64(reader.GetOrdinal("table_size_bytes")),
            IndexBytes: reader.IsDBNull(reader.GetOrdinal("indexes_size_bytes")) ? null : reader.GetInt64(reader.GetOrdinal("indexes_size_bytes")),
            TotalBytesPretty: reader.IsDBNull(reader.GetOrdinal("total_relation_size_pretty")) ? null : reader.GetString(reader.GetOrdinal("total_relation_size_pretty")),
            TableBytesPretty: reader.IsDBNull(reader.GetOrdinal("table_size_pretty")) ? null : reader.GetString(reader.GetOrdinal("table_size_pretty")),
            IndexBytesPretty: reader.IsDBNull(reader.GetOrdinal("indexes_size_pretty")) ? null : reader.GetString(reader.GetOrdinal("indexes_size_pretty")),
            LastAnalyze: reader.IsDBNull(reader.GetOrdinal("last_analyze")) ? null : reader.GetDateTime(reader.GetOrdinal("last_analyze")),
            LastAutoAnalyze: reader.IsDBNull(reader.GetOrdinal("last_autoanalyze")) ? null : reader.GetDateTime(reader.GetOrdinal("last_autoanalyze")),
            LastVacuum: reader.IsDBNull(reader.GetOrdinal("last_vacuum")) ? null : reader.GetDateTime(reader.GetOrdinal("last_vacuum")),
            LastAutoVacuum: reader.IsDBNull(reader.GetOrdinal("last_autovacuum")) ? null : reader.GetDateTime(reader.GetOrdinal("last_autovacuum"))
        );
    }

    private static async Task<IReadOnlyList<ColumnMetadata>> LoadColumnMetadataAsync(NpgsqlConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ColumnMetadataSql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);

        var columns = new List<ColumnMetadata>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnMetadata(
                Position: reader.GetInt32(reader.GetOrdinal("ordinal_position")),
                Name: reader.GetString(reader.GetOrdinal("column_name")),
                DataType: reader.GetString(reader.GetOrdinal("data_type")),
                PostgresType: reader.GetString(reader.GetOrdinal("udt_name")),
                FormattedType: reader.GetString(reader.GetOrdinal("formatted_type")),
                IsNullable: reader.GetBoolean(reader.GetOrdinal("is_nullable")),
                IsPrimaryKey: reader.GetBoolean(reader.GetOrdinal("is_primary_key")),
                IsIdentity: reader.GetBoolean(reader.GetOrdinal("is_identity")),
                IdentityGeneration: reader.IsDBNull(reader.GetOrdinal("identity_generation")) ? null : reader.GetString(reader.GetOrdinal("identity_generation")),
                DefaultValue: reader.IsDBNull(reader.GetOrdinal("column_default")) ? null : reader.GetString(reader.GetOrdinal("column_default")),
                CharacterLength: reader.IsDBNull(reader.GetOrdinal("character_maximum_length")) ? null : reader.GetInt32(reader.GetOrdinal("character_maximum_length")),
                NumericPrecision: reader.IsDBNull(reader.GetOrdinal("numeric_precision")) ? null : reader.GetInt32(reader.GetOrdinal("numeric_precision")),
                NumericScale: reader.IsDBNull(reader.GetOrdinal("numeric_scale")) ? null : reader.GetInt32(reader.GetOrdinal("numeric_scale")),
                DateTimePrecision: reader.IsDBNull(reader.GetOrdinal("datetime_precision")) ? null : reader.GetInt32(reader.GetOrdinal("datetime_precision")),
                Collation: reader.IsDBNull(reader.GetOrdinal("collation_name")) ? null : reader.GetString(reader.GetOrdinal("collation_name")),
                Comment: reader.IsDBNull(reader.GetOrdinal("column_comment")) ? null : reader.GetString(reader.GetOrdinal("column_comment"))
            ));
        }

        return columns;
    }

    private const string TableMetadataSql = """
SELECT
    t.table_schema,
    t.table_name,
    t.table_type,
    pg_get_userbyid(c.relowner) AS table_owner,
    NULLIF(obj_description(c.oid, 'pg_class'), '') AS table_comment,
    pg_total_relation_size(c.oid) AS total_relation_size_bytes,
    pg_table_size(c.oid) AS table_size_bytes,
    pg_indexes_size(c.oid) AS indexes_size_bytes,
    pg_size_pretty(pg_total_relation_size(c.oid)) AS total_relation_size_pretty,
    pg_size_pretty(pg_table_size(c.oid)) AS table_size_pretty,
    pg_size_pretty(pg_indexes_size(c.oid)) AS indexes_size_pretty,
    stats.n_live_tup AS approximate_row_count,
    stats.last_analyze,
    stats.last_autoanalyze,
    stats.last_vacuum,
    stats.last_autovacuum
FROM information_schema.tables t
JOIN pg_catalog.pg_class c
    ON c.relname = t.table_name
JOIN pg_catalog.pg_namespace n
    ON n.oid = c.relnamespace
    AND n.nspname = t.table_schema
LEFT JOIN pg_catalog.pg_stat_all_tables stats
    ON stats.relid = c.oid
WHERE t.table_schema = @schema
  AND t.table_name = @table
LIMIT 1;
""";

    private const string ColumnMetadataSql = """
SELECT
    cols.ordinal_position,
    cols.column_name,
    cols.data_type,
    cols.udt_name,
    CASE
        WHEN cols.character_maximum_length IS NOT NULL
            AND cols.data_type ILIKE 'character varying%'
            THEN FORMAT('%s(%s)', cols.data_type, cols.character_maximum_length)
        WHEN cols.character_maximum_length IS NOT NULL
            AND cols.data_type ILIKE 'character%'
            THEN FORMAT('%s(%s)', cols.data_type, cols.character_maximum_length)
        WHEN cols.numeric_precision IS NOT NULL
            AND cols.numeric_scale IS NOT NULL
            THEN FORMAT('%s(%s,%s)', cols.data_type, cols.numeric_precision, cols.numeric_scale)
        WHEN cols.numeric_precision IS NOT NULL
            THEN FORMAT('%s(%s)', cols.data_type, cols.numeric_precision)
        WHEN cols.datetime_precision IS NOT NULL
            THEN FORMAT('%s(%s)', cols.data_type, cols.datetime_precision)
        ELSE cols.data_type
    END AS formatted_type,
    cols.is_nullable = 'YES' AS is_nullable,
    cols.column_default,
    cols.character_maximum_length,
    cols.numeric_precision,
    cols.numeric_scale,
    cols.datetime_precision,
    cols.is_identity = 'YES' AS is_identity,
    cols.identity_generation,
    cols.collation_name,
    pg_catalog.col_description(c.oid, cols.ordinal_position) AS column_comment,
    EXISTS (
        SELECT 1
        FROM pg_catalog.pg_index i
        JOIN pg_catalog.pg_attribute a
            ON a.attrelid = i.indrelid
           AND a.attnum = ANY(i.indkey)
        WHERE i.indrelid = c.oid
          AND i.indisprimary
          AND a.attname = cols.column_name
    ) AS is_primary_key
FROM information_schema.columns cols
JOIN pg_catalog.pg_class c
    ON c.relname = cols.table_name
JOIN pg_catalog.pg_namespace n
    ON n.oid = c.relnamespace
    AND n.nspname = cols.table_schema
WHERE cols.table_schema = @schema
  AND cols.table_name = @table
ORDER BY cols.ordinal_position;
""";
}

public static class TableSchemaFormatter
{
    public static object CreateSerializableResponse(TableSchemaResult result) => new
    {
        table = new
        {
            schema = result.Table.Schema,
            name = result.Table.Name,
            qualified_name = $"{result.Table.Schema}.{result.Table.Name}",
            type = result.Table.Type,
            owner = result.Table.Owner,
            comment = result.Table.Comment,
            stats = new
            {
                approximate_row_count = result.Table.ApproximateRowCount,
                last_analyze = result.Table.LastAnalyze,
                last_autoanalyze = result.Table.LastAutoAnalyze,
                last_vacuum = result.Table.LastVacuum,
                last_autovacuum = result.Table.LastAutoVacuum
            },
            storage = new
            {
                total_bytes = result.Table.TotalBytes,
                total_pretty = result.Table.TotalBytesPretty,
                table_bytes = result.Table.TableBytes,
                table_pretty = result.Table.TableBytesPretty,
                indexes_bytes = result.Table.IndexBytes,
                indexes_pretty = result.Table.IndexBytesPretty
            }
        },
        columns = result.Columns
            .OrderBy(c => c.Position)
            .Select(c => new
            {
                position = c.Position,
                name = c.Name,
                data_type = c.FormattedType,
                postgres_type = c.PostgresType,
                is_nullable = c.IsNullable,
                is_primary_key = c.IsPrimaryKey,
                is_identity = c.IsIdentity,
                identity_generation = c.IdentityGeneration,
                default_value = c.DefaultValue,
                character_length = c.CharacterLength,
                numeric_precision = c.NumericPrecision,
                numeric_scale = c.NumericScale,
                datetime_precision = c.DateTimePrecision,
                collation = c.Collation,
                comment = c.Comment,
                raw_data_type = c.DataType
            })
    };
}

public sealed record TableSchemaResult(TableMetadata Table, IReadOnlyList<ColumnMetadata> Columns);

public sealed record TableMetadata(
    string Schema,
    string Name,
    string Type,
    string Owner,
    string? Comment,
    long? ApproximateRowCount,
    long? TotalBytes,
    long? TableBytes,
    long? IndexBytes,
    string? TotalBytesPretty,
    string? TableBytesPretty,
    string? IndexBytesPretty,
    DateTime? LastAnalyze,
    DateTime? LastAutoAnalyze,
    DateTime? LastVacuum,
    DateTime? LastAutoVacuum);

public sealed record ColumnMetadata(
    int Position,
    string Name,
    string DataType,
    string PostgresType,
    string FormattedType,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsIdentity,
    string? IdentityGeneration,
    string? DefaultValue,
    int? CharacterLength,
    int? NumericPrecision,
    int? NumericScale,
    int? DateTimePrecision,
    string? Collation,
    string? Comment);
