using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BazarBin.Mcp.Server;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace BazarBin.Tests.Integration;

public sealed class PromptEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PromptEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetPrompt_ReturnsComposedPrompt()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/v1/prompt/sales-insights");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PromptResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        payload.Should().NotBeNull();
        payload!.DatasetId.Should().Be("sales-insights");
        payload.OriginalPrompt.Should().NotBeNullOrWhiteSpace();
        payload.SchemaComment.Should().StartWith("/* Table Schema");
        payload.FinalPrompt.Should().StartWith(payload.SchemaComment);
        payload.FinalPrompt.Should().Contain(payload.OriginalPrompt);
        payload.AiResponse.Should().Contain("simulated:gpt-sim", because: "the simulated client should echo provider info");
    }

    private sealed record PromptResponse(string DatasetId, string OriginalPrompt, string SchemaComment, string FinalPrompt, string AiResponse);
}
