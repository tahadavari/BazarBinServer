namespace BazarBin.Application.Prompts.Contracts;

public sealed class AiCompletionResponse
{
    public AiCompletionResponse(string datasetId, string prompt, string responseText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseText);

        DatasetId = datasetId;
        Prompt = prompt;
        ResponseText = responseText;
    }

    public string DatasetId { get; }

    public string Prompt { get; }

    public string ResponseText { get; }
}
