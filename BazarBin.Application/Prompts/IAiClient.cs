using BazarBin.Application.Prompts.Contracts;

namespace BazarBin.Application.Prompts;

public interface IAiClient
{
    Task<AiCompletionResponse> GetCompletionAsync(AiCompletionRequest request, CancellationToken cancellationToken);
}
