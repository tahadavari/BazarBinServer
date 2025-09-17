namespace BazarBin.Application.Prompts.Contracts;

public sealed class GetPromptResponseRequest
{
    public GetPromptResponseRequest(string? datasetId)
    {
        DatasetId = datasetId ?? string.Empty;
    }

    public string DatasetId { get; }
}
