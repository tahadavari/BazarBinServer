using System.ComponentModel;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using BazarBin.Services;
using ModelContextProtocol.Server;
using Npgsql;
using System.Linq;

namespace BazarBin.Mcp.Server.Mcps;

[McpServerToolType]
public class DataSetMcpService(IConfiguration configuration, ILogger<DataSetMcpService> logger, TableSchemaService tableSchemaService)
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
    private readonly TableSchemaService _tableSchemaService = tableSchemaService;

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
            var schemaResult = await _tableSchemaService.GetTableSchemaAsync(normalizedSchema, normalizedTable, cancellationToken);
            var response = TableSchemaFormatter.CreateSerializableResponse(schemaResult);

            return JsonSerializer.Serialize(response, _serializerOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Validation error while loading schema through MCP.");
            return SerializeError("get_table_schema", ex);
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

}

