using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace BazarBin.Services;

public interface IMcpClientProvider
{
    Task<IMcpClient> GetClientAsync(CancellationToken cancellationToken = default);
    Task<IMcpClient> RefreshClientAsync(CancellationToken cancellationToken = default);
}

public sealed class McpClientProvider : IHostedService, IMcpClientProvider, IAsyncDisposable
{
    private readonly ILogger<McpClientProvider> _logger;
    private readonly string _serverProjectPath;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly object _disposeLock = new();

    private Task<IMcpClient>? _clientTask;

    public McpClientProvider(IHostEnvironment environment, ILogger<McpClientProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _serverProjectPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "BazarBin.Mcp.Server"));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to warm up MCP tools during startup. Tools will be loaded on demand.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task<IMcpClient>? clientTask;

        lock (_disposeLock)
        {
            clientTask = _clientTask;
            _clientTask = null;
        }

        if (clientTask is null)
        {
            return;
        }

        try
        {
            var client = await clientTask.ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose MCP client cleanly.");
        }
    }

    public Task<IMcpClient> GetClientAsync(CancellationToken cancellationToken = default)
        => GetOrCreateClientAsync(cancellationToken);

    public async Task<IMcpClient> RefreshClientAsync(CancellationToken cancellationToken = default)
    {
        await DisposeAsync().ConfigureAwait(false);
        return await GetOrCreateClientAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IMcpClient> GetOrCreateClientAsync(CancellationToken cancellationToken)
    {
        var clientTask = Volatile.Read(ref _clientTask);
        if (clientTask is not null && !clientTask.IsFaulted && !clientTask.IsCanceled)
        {
            return await clientTask.ConfigureAwait(false);
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_clientTask is null || _clientTask.IsFaulted || _clientTask.IsCanceled)
            {
                _clientTask = CreateClientAsync(cancellationToken);
            }

            return await _clientTask.ConfigureAwait(false);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<IMcpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MCP server using project at {ProjectPath}.", _serverProjectPath);

        var options = new StdioClientTransportOptions
        {
            Name = "BazarBin",
            Command = "dotnet",
            Arguments = new[]
            {
                "run",
                "--project",
                _serverProjectPath,
                "--no-build"
            }
        };

        try
        {
            var client = await McpClientFactory.CreateAsync(new StdioClientTransport(options), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("MCP server is ready.");
            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MCP server. Retry after resolving startup issues.");
            throw;
        }
    }
}
