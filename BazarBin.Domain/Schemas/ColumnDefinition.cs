namespace BazarBin.Domain.Schemas;

public sealed class ColumnDefinition
{
    public ColumnDefinition(string name, string dataType, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataType);

        Name = name;
        DataType = dataType;
        Description = description;
    }

    public string Name { get; }

    public string DataType { get; }

    public string? Description { get; }
}
