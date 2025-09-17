namespace BazarBin.Mcp.Server.Contracts;

public sealed record PromptResponseDto(
    string DatasetId,
    string OriginalPrompt,
    string SchemaComment,
    string FinalPrompt,
    string AiResponse);
