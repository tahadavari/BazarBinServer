using Microsoft.Extensions.Options;

namespace BazarBin.Infrastructure.Configuration;

public sealed class PromptDatasetsOptionsValidator : IValidateOptions<PromptDatasetsOptions>
{
    public ValidateOptionsResult Validate(string? name, PromptDatasetsOptions options)
    {
        if (options.Datasets.Count == 0)
        {
            return ValidateOptionsResult.Fail("At least one prompt dataset must be configured.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataset in options.Datasets)
        {
            if (string.IsNullOrWhiteSpace(dataset.Id))
            {
                return ValidateOptionsResult.Fail("Dataset id cannot be empty.");
            }

            if (!ids.Add(dataset.Id))
            {
                return ValidateOptionsResult.Fail($"Duplicate dataset id '{dataset.Id}' detected.");
            }

            if (string.IsNullOrWhiteSpace(dataset.Prompt))
            {
                return ValidateOptionsResult.Fail($"Dataset '{dataset.Id}' must define a prompt.");
            }

            if (dataset.Tables.Count == 0)
            {
                return ValidateOptionsResult.Fail($"Dataset '{dataset.Id}' must define at least one table schema.");
            }

            foreach (var table in dataset.Tables)
            {
                if (string.IsNullOrWhiteSpace(table.Name))
                {
                    return ValidateOptionsResult.Fail($"Dataset '{dataset.Id}' contains a table with an empty name.");
                }

                if (table.Columns.Count == 0)
                {
                    return ValidateOptionsResult.Fail($"Table '{table.Name}' in dataset '{dataset.Id}' must contain at least one column.");
                }

                foreach (var column in table.Columns)
                {
                    if (string.IsNullOrWhiteSpace(column.Name))
                    {
                        return ValidateOptionsResult.Fail($"Table '{table.Name}' in dataset '{dataset.Id}' has a column with an empty name.");
                    }

                    if (string.IsNullOrWhiteSpace(column.DataType))
                    {
                        return ValidateOptionsResult.Fail($"Column '{column.Name}' in table '{table.Name}' must define a data type.");
                    }
                }
            }
        }

        return ValidateOptionsResult.Success;
    }
}
