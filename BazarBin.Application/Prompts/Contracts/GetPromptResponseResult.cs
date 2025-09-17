namespace BazarBin.Application.Prompts.Contracts;

public sealed class GetPromptResponseResult
{
    public GetPromptResponseResult(
        string datasetId,
        string originalPrompt,
        string finalPrompt,
        string schemaComment,
        string aiResponse)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaComment);
        ArgumentException.ThrowIfNullOrWhiteSpace(aiResponse);

        DatasetId = datasetId;
        OriginalPrompt = originalPrompt;
        FinalPrompt = finalPrompt;
        SchemaComment = schemaComment;
        AiResponse = aiResponse;
    }

    public string DatasetId { get; }

    public string OriginalPrompt { get; }

    public string FinalPrompt { get; }

    public string SchemaComment { get; }

    public string AiResponse { get; }
}
