using System.IO;
using BazarBin.Mcp.Server.Logging;
using BazarBin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var logDirectory = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Logs"));
var logFilePath = Path.Combine(logDirectory, "mcp-server.log");
var minimumLogLevel = builder.Configuration.GetValue("Logging:File:MinLevel", LogLevel.Information);

builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileLoggerProvider(logFilePath, minimumLogLevel));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

builder.Services.AddSingleton<TableSchemaService>();
builder.Services.AddHttpClient();

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BazarBin.Mcp.Server");
startupLogger.LogInformation("MCP server started. Writing logs to {LogFilePath}.", logFilePath);

app.UseHttpsRedirection();

app.Run();
