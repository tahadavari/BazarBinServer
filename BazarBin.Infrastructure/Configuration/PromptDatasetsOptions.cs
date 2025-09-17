using System.ComponentModel.DataAnnotations;

namespace BazarBin.Infrastructure.Configuration;

public sealed class PromptDatasetsOptions
{
    public const string SectionName = "PromptDatasets";

    [Required]
    public IList<PromptDatasetOptions> Datasets { get; init; } = new List<PromptDatasetOptions>();
}

public sealed class PromptDatasetOptions
{
    [Required]
    public string Id { get; init; } = string.Empty;

    [Required]
    public string Prompt { get; init; } = string.Empty;

    [Required]
    public IList<TableOptions> Tables { get; init; } = new List<TableOptions>();
}

public sealed class TableOptions
{
    [Required]
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    [Required]
    public IList<ColumnOptions> Columns { get; init; } = new List<ColumnOptions>();
}

public sealed class ColumnOptions
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string DataType { get; init; } = string.Empty;

    public string? Description { get; init; }
}
