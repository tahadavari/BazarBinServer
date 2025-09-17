namespace BazarBin.Infrastructure.Ai;

public sealed class AiClientOptions
{
    public const string SectionName = "Ai";

    public string ProviderName { get; init; } = "simulated";

    public string Model { get; init; } = "mock";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
