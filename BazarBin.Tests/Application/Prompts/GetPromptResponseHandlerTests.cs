using BazarBin.Application.Common;
using BazarBin.Application.Prompts;
using BazarBin.Application.Prompts.Contracts;
using BazarBin.Application.Schemas;
using BazarBin.Domain.Prompts;
using BazarBin.Domain.Schemas;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BazarBin.Tests.Application.Prompts;

public sealed class GetPromptResponseHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenDatasetExists_ComposesPromptAndReturnsResponse()
    {
        var repository = Substitute.For<IPromptRepository>();
        var schemaProvider = Substitute.For<ITableSchemaProvider>();
        var commentBuilder = Substitute.For<ITableSchemaCommentBuilder>();
        var aiClient = Substitute.For<IAiClient>();
        var handler = new GetPromptResponseHandler(repository, schemaProvider, commentBuilder, aiClient, NullLogger<GetPromptResponseHandler>.Instance);

        var dataset = new PromptDataset("sales", "Explain recent sales trends.");
        var schema = new TableSchema("sales", new[]
        {
            new TableDefinition("orders", "", new[] { new ColumnDefinition("id", "uuid", "") })
        });

        repository.GetByIdAsync(dataset.Id, Arg.Any<CancellationToken>()).Returns(dataset);
        schemaProvider.GetSchemaAsync(dataset.Id, Arg.Any<CancellationToken>()).Returns(schema);
        commentBuilder.BuildComment(schema).Returns("/*schema*/");
        aiClient.GetCompletionAsync(Arg.Any<AiCompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<AiCompletionRequest>();
                return Task.FromResult(new AiCompletionResponse(request.DatasetId, request.Prompt, "response"));
            });

        var result = await handler.HandleAsync(new GetPromptResponseRequest(dataset.Id), CancellationToken.None);

        result.DatasetId.Should().Be(dataset.Id);
        result.SchemaComment.Should().Be("/*schema*/");
        result.OriginalPrompt.Should().Be(dataset.Prompt);
        result.FinalPrompt.Should().Be("/*schema*/\n\n" + dataset.Prompt);
        result.AiResponse.Should().Be("response");

        await aiClient.Received(1).GetCompletionAsync(Arg.Is<AiCompletionRequest>(r => r.Prompt == result.FinalPrompt), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDatasetNotFound_Throws()
    {
        var repository = Substitute.For<IPromptRepository>();
        var schemaProvider = Substitute.For<ITableSchemaProvider>();
        var commentBuilder = Substitute.For<ITableSchemaCommentBuilder>();
        var aiClient = Substitute.For<IAiClient>();
        var handler = new GetPromptResponseHandler(repository, schemaProvider, commentBuilder, aiClient, NullLogger<GetPromptResponseHandler>.Instance);

        repository.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((PromptDataset?)null);

        var act = () => handler.HandleAsync(new GetPromptResponseRequest("missing"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WhenSchemaNotFound_Throws()
    {
        var repository = Substitute.For<IPromptRepository>();
        var schemaProvider = Substitute.For<ITableSchemaProvider>();
        var commentBuilder = Substitute.For<ITableSchemaCommentBuilder>();
        var aiClient = Substitute.For<IAiClient>();
        var handler = new GetPromptResponseHandler(repository, schemaProvider, commentBuilder, aiClient, NullLogger<GetPromptResponseHandler>.Instance);

        var dataset = new PromptDataset("sales", "Explain recent sales trends.");
        repository.GetByIdAsync(dataset.Id, Arg.Any<CancellationToken>()).Returns(dataset);
        schemaProvider.GetSchemaAsync(dataset.Id, Arg.Any<CancellationToken>()).Returns((TableSchema?)null);

        var act = () => handler.HandleAsync(new GetPromptResponseRequest(dataset.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WhenDatasetIdMissing_ThrowsValidationException()
    {
        var repository = Substitute.For<IPromptRepository>();
        var schemaProvider = Substitute.For<ITableSchemaProvider>();
        var commentBuilder = Substitute.For<ITableSchemaCommentBuilder>();
        var aiClient = Substitute.For<IAiClient>();
        var handler = new GetPromptResponseHandler(repository, schemaProvider, commentBuilder, aiClient, NullLogger<GetPromptResponseHandler>.Instance);

        var act = () => handler.HandleAsync(new GetPromptResponseRequest(" "), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
