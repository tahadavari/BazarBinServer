namespace BazarBin.Models;

public class DataSet
{
    public int Id { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; }
}
