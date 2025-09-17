namespace BazarBin.Models;

public sealed record PromptRequest(string Prompt);

public sealed record PromptResponse(string PromptWithSchema, string Response);
