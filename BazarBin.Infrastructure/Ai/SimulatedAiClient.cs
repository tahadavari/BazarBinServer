using BazarBin.Application.Prompts;
using BazarBin.Application.Prompts.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BazarBin.Infrastructure.Ai;

public sealed class SimulatedAiClient : IAiClient
{
    private readonly IOptionsMonitor<AiClientOptions> optionsMonitor;
    private readonly ILogger<SimulatedAiClient> logger;

    public SimulatedAiClient(IOptionsMonitor<AiClientOptions> optionsMonitor, ILogger<SimulatedAiClient> logger)
    {
        this.optionsMonitor = optionsMonitor;
        this.logger = logger;
    }

    public Task<AiCompletionResponse> GetCompletionAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = optionsMonitor.CurrentValue;
        logger.LogInformation(
            "Simulating AI response for dataset {DatasetId} using provider {Provider} and model {Model}.",
            request.DatasetId,
            options.ProviderName,
            options.Model);

        var responseText = $"Simulated answer from {options.ProviderName}:{options.Model} for dataset {request.DatasetId}.";
        return Task.FromResult(new AiCompletionResponse(request.DatasetId, request.Prompt, responseText));
    }
}
