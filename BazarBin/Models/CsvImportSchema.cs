namespace BazarBin.Models;

public sealed record CsvColumnSchema(
    string Name,
    string DbType,
    bool Include = true,
    string? Comment = null);

public sealed record CsvImportSchema(
    string TableName,
    IReadOnlyList<CsvColumnSchema> Columns,
    string? TableComment = null,
    bool FirstRowIsHeader = true);

public sealed record CsvImportResult(
    string FullyQualifiedTableName,
    int RowsInserted);
