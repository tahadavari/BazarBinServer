namespace BazarBin.Application.Prompts.Contracts;

public sealed class AiCompletionRequest
{
    public AiCompletionRequest(string datasetId, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        DatasetId = datasetId;
        Prompt = prompt;
    }

    public string DatasetId { get; }

    public string Prompt { get; }
}
