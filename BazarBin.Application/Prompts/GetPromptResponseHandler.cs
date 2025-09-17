using BazarBin.Application.Common;
using BazarBin.Application.Prompts.Contracts;
using BazarBin.Application.Schemas;
using BazarBin.Domain.Prompts;
using Microsoft.Extensions.Logging;

namespace BazarBin.Application.Prompts;

public sealed class GetPromptResponseHandler
{
    private readonly IPromptRepository promptRepository;
    private readonly ITableSchemaProvider tableSchemaProvider;
    private readonly ITableSchemaCommentBuilder tableSchemaCommentBuilder;
    private readonly IAiClient aiClient;
    private readonly ILogger<GetPromptResponseHandler> logger;

    public GetPromptResponseHandler(
        IPromptRepository promptRepository,
        ITableSchemaProvider tableSchemaProvider,
        ITableSchemaCommentBuilder tableSchemaCommentBuilder,
        IAiClient aiClient,
        ILogger<GetPromptResponseHandler> logger)
    {
        this.promptRepository = promptRepository;
        this.tableSchemaProvider = tableSchemaProvider;
        this.tableSchemaCommentBuilder = tableSchemaCommentBuilder;
        this.aiClient = aiClient;
        this.logger = logger;
    }

    public async Task<GetPromptResponseResult> HandleAsync(GetPromptResponseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DatasetId))
        {
            throw new ValidationException("Dataset id is required.", ErrorCodes.DatasetIdRequired);
        }

        logger.LogInformation("Fetching prompt for dataset {DatasetId}.", request.DatasetId);
        PromptDataset? dataset = await promptRepository.GetByIdAsync(request.DatasetId, cancellationToken).ConfigureAwait(false);
        if (dataset is null)
        {
            throw new NotFoundException($"Dataset '{request.DatasetId}' was not found.", ErrorCodes.PromptDatasetNotFound);
        }

        logger.LogInformation("Fetching table schema for dataset {DatasetId}.", dataset.Id);
        var schema = await tableSchemaProvider.GetSchemaAsync(dataset.Id, cancellationToken).ConfigureAwait(false);
        if (schema is null)
        {
            throw new NotFoundException($"Schema for dataset '{dataset.Id}' was not found.", ErrorCodes.SchemaNotFound);
        }

        var schemaComment = tableSchemaCommentBuilder.BuildComment(schema);
        var finalPrompt = string.Join(Environment.NewLine + Environment.NewLine, schemaComment, dataset.Prompt);

        logger.LogInformation("Sending composed prompt to AI client for dataset {DatasetId}.", dataset.Id);
        var aiRequest = new AiCompletionRequest(dataset.Id, finalPrompt);
        var completion = await aiClient.GetCompletionAsync(aiRequest, cancellationToken).ConfigureAwait(false);

        return new GetPromptResponseResult(dataset.Id, dataset.Prompt, finalPrompt, schemaComment, completion.ResponseText);
    }
}
