using BazarBin.Domain.Prompts;

namespace BazarBin.Application.Prompts;

public interface IPromptRepository
{
    Task<PromptDataset?> GetByIdAsync(string datasetId, CancellationToken cancellationToken);
}
