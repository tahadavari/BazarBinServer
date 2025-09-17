namespace BazarBin.Domain.Prompts;

public sealed class PromptDataset
{
    public PromptDataset(string id, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        Id = id;
        Prompt = prompt;
    }

    public string Id { get; }

    public string Prompt { get; }
}
