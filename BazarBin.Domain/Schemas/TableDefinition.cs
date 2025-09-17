namespace BazarBin.Domain.Schemas;

public sealed class TableDefinition
{
    public TableDefinition(string name, string? description, IReadOnlyCollection<ColumnDefinition> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(columns);

        Name = name;
        Description = description;
        Columns = columns.ToArray();
    }

    public string Name { get; }

    public string? Description { get; }

    public IReadOnlyCollection<ColumnDefinition> Columns { get; }
}
