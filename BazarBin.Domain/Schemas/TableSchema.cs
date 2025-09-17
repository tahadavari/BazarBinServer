namespace BazarBin.Domain.Schemas;

public sealed class TableSchema
{
    public TableSchema(string datasetId, IReadOnlyCollection<TableDefinition> tables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        ArgumentNullException.ThrowIfNull(tables);

        DatasetId = datasetId;
        Tables = tables.ToArray();
    }

    public string DatasetId { get; }

    public IReadOnlyCollection<TableDefinition> Tables { get; }
}
