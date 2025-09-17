using BazarBin.Application.Schemas;
using BazarBin.Domain.Schemas;
using BazarBin.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BazarBin.Infrastructure.Schemas;

public sealed class ConfigurationTableSchemaProvider : ITableSchemaProvider
{
    private readonly IOptionsMonitor<PromptDatasetsOptions> options;

    public ConfigurationTableSchemaProvider(IOptionsMonitor<PromptDatasetsOptions> options)
    {
        this.options = options;
    }

    public Task<TableSchema?> GetSchemaAsync(string datasetId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        var dataset = options.CurrentValue.Datasets
            .FirstOrDefault(x => string.Equals(x.Id, datasetId, StringComparison.OrdinalIgnoreCase));

        if (dataset is null)
        {
            return Task.FromResult<TableSchema?>(null);
        }

        var tables = dataset.Tables
            .Select(table => new TableDefinition(
                table.Name,
                table.Description,
                table.Columns.Select(column => new ColumnDefinition(column.Name, column.DataType, column.Description)).ToArray()))
            .ToArray();

        var schema = new TableSchema(dataset.Id, tables);
        return Task.FromResult<TableSchema?>(schema);
    }
}
