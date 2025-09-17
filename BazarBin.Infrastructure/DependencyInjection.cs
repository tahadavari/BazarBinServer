using BazarBin.Application.Prompts;
using BazarBin.Application.Schemas;
using BazarBin.Infrastructure.Ai;
using BazarBin.Infrastructure.Configuration;
using BazarBin.Infrastructure.Prompts;
using BazarBin.Infrastructure.Schemas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BazarBin.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBazarBinInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PromptDatasetsOptions>()
            .Bind(configuration.GetSection(PromptDatasetsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PromptDatasetsOptions>, PromptDatasetsOptionsValidator>();

        services.AddOptions<AiClientOptions>()
            .Bind(configuration.GetSection(AiClientOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IPromptRepository, ConfigurationPromptRepository>();
        services.AddSingleton<ITableSchemaProvider, ConfigurationTableSchemaProvider>();
        services.AddSingleton<ITableSchemaCommentBuilder, TableSchemaCommentBuilder>();
        services.AddSingleton<IAiClient, SimulatedAiClient>();
        services.AddScoped<GetPromptResponseHandler>();

        return services;
    }
}
