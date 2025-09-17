using System.Globalization;
using System.Text.RegularExpressions;
using BazarBin.Data;
using BazarBin.Models;
using BazarBin.Options;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace BazarBin.Services;

public interface ICsvImportService
{
    Task<CsvImportResult> ImportAsync(Stream csvStream, CsvImportSchema schema, CancellationToken cancellationToken = default);
}

public sealed partial class CsvImportService : ICsvImportService
{
    private static readonly Regex IdentifierRegex = MyRegex();
    private static readonly Regex DbTypeRegex = MyRegex1();

    private readonly string _connectionString;
    private readonly ImportOptions _options;
    private readonly ILogger<CsvImportService> _logger;
    private readonly ApplicationDbContext _dbContext;

    public CsvImportService(IConfiguration configuration, IOptions<ImportOptions> options, ILogger<CsvImportService> logger, ApplicationDbContext dbContext)
    {
        _connectionString = configuration.GetConnectionString("ImportDatabase")
                           ?? throw new InvalidOperationException("Connection string 'ImportDatabase' is not configured.");
        _options = options.Value;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task<CsvImportResult> ImportAsync(Stream csvStream, CsvImportSchema schema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csvStream);
        ArgumentNullException.ThrowIfNull(schema);

        var orderedColumns = schema.Columns ?? throw new InvalidOperationException("Schema must include columns.");
        if (orderedColumns.Count == 0)
        {
            throw new InvalidOperationException("Schema must include at least one column.");
        }

        var targetSchema = _options.DefaultSchema?.Trim();

        if (string.IsNullOrWhiteSpace(targetSchema))
        {
            throw new InvalidOperationException("Import schema default is required.");
        }

        if (string.Equals(targetSchema, "public", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Import schema must be different from the primary application schema.");
        }

        ValidateIdentifier(schema.TableName, nameof(schema.TableName));
        ValidateIdentifier(targetSchema, nameof(ImportOptions.DefaultSchema));

        foreach (var (column, index) in orderedColumns.Select((c, i) => (c, i)))
        {
            ValidateIdentifier(column.Name, $"Column[{index}].Name");
            ValidateDbType(column.DbType, column.Name);
        }

        var includedColumns = orderedColumns
            .Select((column, index) => (column, index))
            .Where(item => item.column.Include)
            .ToArray();

        if (includedColumns.Length == 0)
        {
            throw new InvalidOperationException("At least one column must be marked for inclusion.");
        }

        if (csvStream.CanSeek)
        {
            csvStream.Seek(0, SeekOrigin.Begin);
        }

        var hasHeaderRecord = schema.FirstRowIsHeader;

        var csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = hasHeaderRecord,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectDelimiter = true
        };

        using var reader = new StreamReader(csvStream, leaveOpen: true);
        using var csv = new CsvReader(reader, csvConfiguration);

        if (!csv.Read())
        {
            throw new InvalidOperationException("CSV file is empty.");
        }

        if (hasHeaderRecord)
        {
            csv.ReadHeader();
            var header = csv.HeaderRecord ?? Array.Empty<string>();
            if (header.Length == 0)
            {
                throw new InvalidOperationException("CSV file must contain a header row.");
            }

            if (header.Length != orderedColumns.Count ||
                !header.Zip(orderedColumns, (headerName, column) => string.Equals(headerName, column.Name, StringComparison.OrdinalIgnoreCase)).All(match => match))
            {
                throw new InvalidOperationException("CSV header does not match the provided schema.");
            }
        }
        else if (csv.Parser.Count != orderedColumns.Count)
        {
            throw new InvalidOperationException("CSV row column count does not match the provided schema.");
        }

        var columnDefinitions = includedColumns
            .Select(item => CreateColumnDefinition(item.column, item.index))
            .ToArray();

        var qualifiedTableName = $"{QuoteIdentifier(targetSchema)}.{QuoteIdentifier(schema.TableName)}";
        var unquotedQualifiedName = $"{targetSchema}.{schema.TableName}";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await EnsureSchemaExistsAsync(connection, transaction, targetSchema, cancellationToken);
        await RecreateTableAsync(connection, transaction, qualifiedTableName, schema.TableComment, includedColumns.Select(c => c.column).ToArray(), cancellationToken);

        var copyCommand = $"COPY {qualifiedTableName} ({string.Join(", ", columnDefinitions.Select(c => QuoteIdentifier(c.Name)))} ) FROM STDIN (FORMAT BINARY)";

        await using var writer = await connection.BeginBinaryImportAsync(copyCommand, cancellationToken);

        var rowCount = 0;

        async Task WriteCurrentRowAsync()
        {
            await writer.StartRowAsync(cancellationToken);

            foreach (var definition in columnDefinitions)
            {
                var rawValue = csv.GetField(definition.SourceIndex);
                var typedValue = definition.Convert(rawValue);

                if (typedValue is null)
                {
                    await writer.WriteNullAsync(cancellationToken);
                }
                else
                {
                    await writer.WriteAsync(typedValue, definition.DbType, cancellationToken);
                }
            }

            rowCount++;
        }

        if (!hasHeaderRecord)
        {
            await WriteCurrentRowAsync();
        }

        while (await csv.ReadAsync())
        {
            await WriteCurrentRowAsync();
        }

        await writer.CompleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await UpsertDataSetAsync(targetSchema, schema.TableName, cancellationToken);

        _logger.LogInformation("Imported {RowCount} rows into {Table}", rowCount, unquotedQualifiedName);

        return new CsvImportResult(unquotedQualifiedName, rowCount);
    }

    private async Task UpsertDataSetAsync(string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.DataSets
            .SingleOrDefaultAsync(ds => ds.SchemaName == schemaName && ds.TableName == tableName, cancellationToken);

        if (existing is null)
        {
            var dataSet = new DataSet
            {
                SchemaName = schemaName,
                TableName = tableName,
                ImportedAt = DateTimeOffset.UtcNow
            };

            _dbContext.DataSets.Add(dataSet);
        }
        else
        {
            existing.ImportedAt = DateTimeOffset.UtcNow;
            _dbContext.DataSets.Update(existing);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureSchemaExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string schemaName, CancellationToken cancellationToken)
    {
        var createSchemaSql = $"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(schemaName)};";
        await using var createSchemaCommand = new NpgsqlCommand(createSchemaSql, connection, transaction);
        await createSchemaCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecreateTableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string qualifiedTableName, string? tableComment, IReadOnlyList<CsvColumnSchema> includedColumns, CancellationToken cancellationToken)
    {
        var dropSql = $"DROP TABLE IF EXISTS {qualifiedTableName};";
        await using (var dropCommand = new NpgsqlCommand(dropSql, connection, transaction))
        {
            await dropCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var columnsSql = string.Join(", ", includedColumns.Select(col => $"{QuoteIdentifier(col.Name)} {col.DbType.Trim()}"));
        var createSql = $"CREATE TABLE {qualifiedTableName} ({columnsSql});";
        await using var createCommand = new NpgsqlCommand(createSql, connection, transaction);
        await createCommand.ExecuteNonQueryAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(tableComment))
        {
            var tableCommentSql = $"COMMENT ON TABLE {qualifiedTableName} IS @comment;";
            await using var tableCommentCommand = new NpgsqlCommand(tableCommentSql, connection, transaction);
            tableCommentCommand.Parameters.AddWithValue("comment", tableComment);
            await tableCommentCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var column in includedColumns)
        {
            if (column.Comment is null)
            {
                continue;
            }

            var commentSql = $"COMMENT ON COLUMN {qualifiedTableName}.{QuoteIdentifier(column.Name)} IS @comment;";
            await using var commentCommand = new NpgsqlCommand(commentSql, connection, transaction);
            commentCommand.Parameters.AddWithValue("comment", column.Comment);
            await commentCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static ColumnDefinition CreateColumnDefinition(CsvColumnSchema column, int sourceIndex)
    {
        var normalizedType = NormalizeDbType(column.DbType);
        return normalizedType switch
        {
            "boolean" or "bool" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Boolean, ConvertToBool),
            "smallint" or "int2" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Smallint, ConvertToInt16),
            "integer" or "int" or "int4" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Integer, ConvertToInt32),
            "bigint" or "int8" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Bigint, ConvertToInt64),
            "numeric" or "decimal" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Numeric, ConvertToDecimal),
            "real" or "float4" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Real, ConvertToSingle),
            "double precision" or "float8" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Double, ConvertToDouble),
            "text" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Text, ConvertToString),
            "varchar" or "character varying" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Varchar, ConvertToString),
            "char" or "character" or "bpchar" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Char, ConvertToString),
            "uuid" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Uuid, ConvertToGuid),
            "date" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Date, ConvertToDate),
            "timestamp" or "timestamp without time zone" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Timestamp, ConvertToDateTime),
            "timestamptz" or "timestamp with time zone" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.TimestampTz, ConvertToDateTimeOffset),
            "time" or "time without time zone" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Time, ConvertToTime),
            "timetz" or "time with time zone" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.TimeTz, ConvertToTimeWithOffset),
            "json" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Json, ConvertToString),
            "jsonb" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Jsonb, ConvertToString),
            "bytea" => new ColumnDefinition(column.Name, sourceIndex, NpgsqlDbType.Bytea, ConvertToByteArray),
            _ => throw new InvalidOperationException($"Unsupported PostgreSQL column type '{column.DbType}' for column '{column.Name}'.")
        };
    }

    private static string NormalizeDbType(string dbType)
    {
        var trimmed = dbType.Trim().ToLowerInvariant();
        var parenIndex = trimmed.IndexOf('(');
        if (parenIndex >= 0)
        {
            trimmed = trimmed[..parenIndex].TrimEnd();
        }

        return Regex.Replace(trimmed, @"\s+", " ");
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdentifierRegex.IsMatch(value))
        {
            throw new InvalidOperationException($"Invalid identifier for {parameterName}: '{value}'.");
        }
    }

    private static void ValidateDbType(string dbType, string columnName)
    {
        if (string.IsNullOrWhiteSpace(dbType) || !DbTypeRegex.IsMatch(dbType))
        {
            throw new InvalidOperationException($"Invalid PostgreSQL type definition '{dbType}' for column '{columnName}'.");
        }
    }

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private sealed record ColumnDefinition(string Name, int SourceIndex, NpgsqlDbType DbType, Func<string?, object?> Convert);

    private static object? ConvertToString(string? value) => value;

    private static object? ConvertToBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "t" or "1" => true,
            "false" or "f" or "0" => false,
            _ => throw new InvalidOperationException($"Unable to convert '{value}' to boolean.")
        };
    }

    private static object? ConvertToInt16(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return short.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static object? ConvertToInt32(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static object? ConvertToInt64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return long.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static object? ConvertToDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.Parse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static object? ConvertToSingle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return float.Parse(value.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }

    private static object? ConvertToDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.Parse(value.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }

    private static object? ConvertToGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.Parse(value.Trim());
    }

    private static object? ConvertToDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsedDateTime = DateTime.Parse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return DateOnly.FromDateTime(parsedDateTime);
    }

    private static object? ConvertToDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.Parse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object? ConvertToDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object? ConvertToTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TimeOnly.Parse(value.Trim(), CultureInfo.InvariantCulture);
    }

    private static object? ConvertToTimeWithOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object? ConvertToByteArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Convert.FromBase64String(value.Trim());
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
    [GeneratedRegex(@"^[A-Za-z0-9_\s(),]+$", RegexOptions.Compiled)]
    private static partial Regex MyRegex1();
}






