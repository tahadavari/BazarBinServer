using System.ComponentModel;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Server;
using Npgsql;

namespace BazarBin.Mcp.Server.Mcps;

[McpServerToolType]
public class DataSetMcpService(IConfiguration configuration, ILogger<DataSetMcpService> logger)
{
    private const int DefaultRowLimit = 200;
    private const int MaxRowLimit = 5000;

    private readonly ILogger<DataSetMcpService> _logger = logger;
    private readonly string _connectionString = configuration.GetConnectionString("ImportDatabase")
                                                   ?? throw new InvalidOperationException("Connection string 'ImportDatabase' is not configured.");
    private readonly string? _defaultSchema = configuration["ImportOptions:DefaultSchema"]?.Trim();
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [McpServerTool(Name = "ExecuteQuery")]
    [Description("Run a read-only SQL query against the import database and return JSON containing column metadata and result rows for downstream LLM use.")]
    public async Task<string> ExecuteQuery(
        [Description("Full read-only SQL statement to execute. The statement must begin with SELECT or WITH and should schema-qualify tables, for example ingest.products.")]
        string sql,
        [Description("Maximum number of rows to include in the response (1-5000). Leave empty to use the default limit of 200 rows.")]
        int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SerializeError("execute_query", new ArgumentException("Query text is required.", nameof(sql)));
        }

        var trimmedSql = sql.Trim();

        if (!IsReadOnlyQuery(trimmedSql))
        {
            return SerializeError("execute_query", new InvalidOperationException("Only read-only queries that start with SELECT or WITH are permitted."));
        }

        var rowLimit = NormalizeRowLimit(maxRows);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(trimmedSql, connection)
            {
                CommandTimeout = 90
            };

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var columnSchema = await reader.GetColumnSchemaAsync(cancellationToken);

            var rows = new List<IDictionary<string, object?>>();
            var totalRowsRead = 0;

            while (await reader.ReadAsync(cancellationToken))
            {
                totalRowsRead++;

                if (rows.Count >= rowLimit)
                {
                    continue;
                }

                rows.Add(ReadRow(reader, columnSchema));
            }

            var response = new
            {
                query = trimmedSql,
                row_limit = rowLimit,
                rows_returned = rows.Count,
                total_rows_read = totalRowsRead,
                truncated = totalRowsRead > rows.Count,
                columns = columnSchema.Select(CreateSerializableColumn),
                data = rows
            };

            return JsonSerializer.Serialize(response, _serializerOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresException ex)
        {
            _logger.LogWarning(ex, "PostgreSQL error while executing query through MCP.");
            return SerializeError("execute_query", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while executing query through MCP.");
            return SerializeError("execute_query", ex);
        }
    }

    [McpServerTool(Name = "GetTableSchema")]
    [Description("Describe a PostgreSQL table in depth, returning schema-qualified name, comments, statistics, and full column metadata optimized for LLM consumption.")]
    public async Task<string> GetTableSchema(
        [Description("The exact name of the table to inspect, for example products or orders_2024.")]
        string tableName,
        [Description("PostgreSQL schema that owns the table. Leave empty to fall back to the configured import schema (commonly 'ingest').")]
        string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return SerializeError("get_table_schema", new ArgumentException("Table name is required.", nameof(tableName)));
        }

        var resolvedSchema = string.IsNullOrWhiteSpace(schemaName) ? _defaultSchema : schemaName?.Trim();

        if (string.IsNullOrWhiteSpace(resolvedSchema))
        {
            return SerializeError("get_table_schema", new InvalidOperationException("A schema name is required. Configure ImportOptions:DefaultSchema or pass the schema explicitly."));
        }

        var normalizedSchema = resolvedSchema!;
        var normalizedTable = tableName.Trim();

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);

            var tableMetadata = await LoadTableMetadataAsync(connection, normalizedSchema, normalizedTable, cancellationToken);
            if (tableMetadata is null)
            {
                return SerializeError("get_table_schema", new InvalidOperationException($"Table '{normalizedSchema}.{normalizedTable}' was not found."));
            }

            var columns = await LoadColumnMetadataAsync(connection, normalizedSchema, normalizedTable, cancellationToken);

            var response = new
            {
                table = new
                {
                    schema = tableMetadata.Schema,
                    name = tableMetadata.Name,
                    qualified_name = $"{tableMetadata.Schema}.{tableMetadata.Name}",
                    type = tableMetadata.Type,
                    owner = tableMetadata.Owner,
                    comment = tableMetadata.Comment,
                    stats = new
                    {
                        approximate_row_count = tableMetadata.ApproximateRowCount,
                        last_analyze = tableMetadata.LastAnalyze,
                        last_autoanalyze = tableMetadata.LastAutoAnalyze,
                        last_vacuum = tableMetadata.LastVacuum,
                        last_autovacuum = tableMetadata.LastAutoVacuum
                    },
                    storage = new
                    {
                        total_bytes = tableMetadata.TotalBytes,
                        total_pretty = tableMetadata.TotalBytesPretty,
                        table_bytes = tableMetadata.TableBytes,
                        table_pretty = tableMetadata.TableBytesPretty,
                        indexes_bytes = tableMetadata.IndexBytes,
                        indexes_pretty = tableMetadata.IndexBytesPretty
                    }
                },
                columns = columns
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

            return JsonSerializer.Serialize(response, _serializerOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresException ex)
        {
            _logger.LogWarning(ex, "PostgreSQL error while loading schema through MCP.");
            return SerializeError("get_table_schema", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while loading schema through MCP.");
            return SerializeError("get_table_schema", ex);
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static bool IsReadOnlyQuery(string sql)
    {
        var head = StripLeadingComments(sql);
        if (string.IsNullOrWhiteSpace(head))
        {
            return false;
        }

        head = head.TrimStart();

        return head.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
               || head.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripLeadingComments(string sql)
    {
        var span = sql.AsSpan();
        span = span.TrimStart();

        while (true)
        {
            if (span.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = span.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                span = span[(end + 2)..];
                span = span.TrimStart();
                continue;
            }

            if (span.StartsWith("--", StringComparison.Ordinal))
            {
                var newline = span.IndexOfAny('\n', '\r');
                if (newline < 0)
                {
                    span = ReadOnlySpan<char>.Empty;
                    break;
                }

                span = span[(newline + 1)..];
                span = span.TrimStart();
                continue;
            }

            break;
        }

        return span.ToString();
    }

    private static int NormalizeRowLimit(int? requested)
    {
        if (!requested.HasValue)
        {
            return DefaultRowLimit;
        }

        var value = requested.Value;
        if (value < 1)
        {
            return 1;
        }

        return value > MaxRowLimit ? MaxRowLimit : value;
    }

    private static IDictionary<string, object?> ReadRow(NpgsqlDataReader reader, IReadOnlyList<DbColumn> schema)
    {
        var result = new Dictionary<string, object?>(schema.Count, StringComparer.Ordinal);
        foreach (var column in schema)
        {
            if (column.ColumnName is null || column.ColumnOrdinal is not int ordinal)
            {
                continue;
            }

            var value = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            result[column.ColumnName] = NormalizeValue(value);
        }

        return result;
    }

    private static object CreateSerializableColumn(DbColumn column) => new
    {
        name = column.ColumnName,
        ordinal = column.ColumnOrdinal,
        data_type = column.DataType?.Name,
        postgres_type = column.DataTypeName,
        is_nullable = column.AllowDBNull,
        column_size = column.ColumnSize,
        numeric_precision = column.NumericPrecision,
        numeric_scale = column.NumericScale,
        is_key = column.IsKey,
        source_table = column.BaseTableName is null ? null : new
        {
            schema = column.BaseSchemaName,
            name = column.BaseTableName,
            qualified_name = column.BaseSchemaName is null ? column.BaseTableName : $"{column.BaseSchemaName}.{column.BaseTableName}"
        }
    };

    private static object? NormalizeValue(object? value)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return null;
            case DateOnly dateOnly:
                return dateOnly.ToString("O", CultureInfo.InvariantCulture);
            case TimeOnly timeOnly:
                return timeOnly.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            default:
                var type = value.GetType();
                if (type.Namespace?.StartsWith("NpgsqlTypes", StringComparison.Ordinal) == true)
                {
                    return value.ToString();
                }

                return value;
        }
    }

    private async Task<TableMetadata?> LoadTableMetadataAsync(NpgsqlConnection connection, string schema, string table, CancellationToken cancellationToken)
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

    private async Task<IReadOnlyList<ColumnMetadata>> LoadColumnMetadataAsync(NpgsqlConnection connection, string schema, string table, CancellationToken cancellationToken)
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

    private string SerializeError(string context, Exception exception)
    {
        object? postgres = null;

        if (exception is PostgresException pg)
        {
            postgres = new
            {
                sql_state = pg.SqlState,
                detail = pg.Detail,
                hint = pg.Hint,
                position = pg.Position,
                schema = pg.SchemaName,
                table = pg.TableName,
                column = pg.ColumnName
            };
        }

        var payload = new
        {
            context,
            error = new
            {
                type = exception.GetType().Name,
                message = exception.Message,
                postgres
            }
        };

        return JsonSerializer.Serialize(payload, _serializerOptions);
    }

    private sealed record TableMetadata(
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

    private sealed record ColumnMetadata(
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

