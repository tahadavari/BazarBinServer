using System.ClientModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddChatClient(_ =>
        new ChatClient("gpt-4o-mini", new ApiKeyCredential(builder.Configuration["OpenAiKey"]!)
            , new OpenAIClientOptions
            {
                NetworkTimeout = TimeSpan.FromSeconds(100),
            }).AsIChatClient())
    .UseFunctionInvocation(configure: x => { x.IncludeDetailedErrors = true; });

var app = builder.Build();

var chatClient = app.Services.GetRequiredService<IChatClient>();

var mcpClient = await CreateMcpClient();

var tools = await mcpClient.ListToolsAsync();

var prompt = "how will the weather in tehran be in the next 4 days?";

var response = await chatClient.GetResponseAsync(prompt, new ChatOptions() { Tools = [..tools] });

Console.WriteLine(response);
return;

static async Task<IMcpClient> CreateMcpClient() =>
    await McpClientFactory.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "BazarBin",
        Command = "dotnet",
        Arguments = ["run", "--project", @"C:\Projects\Github\BazarBin\BazarBin.Mcp.Server", "--no-build"],
    }));