using BazarBin.Application.Prompts;
using BazarBin.Domain.Prompts;
using BazarBin.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BazarBin.Infrastructure.Prompts;

public sealed class ConfigurationPromptRepository : IPromptRepository
{
    private readonly IOptionsMonitor<PromptDatasetsOptions> options;

    public ConfigurationPromptRepository(IOptionsMonitor<PromptDatasetsOptions> options)
    {
        this.options = options;
    }

    public Task<PromptDataset?> GetByIdAsync(string datasetId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        var dataset = options.CurrentValue.Datasets
            .FirstOrDefault(x => string.Equals(x.Id, datasetId, StringComparison.OrdinalIgnoreCase));

        if (dataset is null)
        {
            return Task.FromResult<PromptDataset?>(null);
        }

        var result = new PromptDataset(dataset.Id, dataset.Prompt);
        return Task.FromResult<PromptDataset?>(result);
    }
}
