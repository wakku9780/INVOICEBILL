using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SareeBilling.Application;
using SareeBilling.Application.Abstractions;
using SareeBilling.Infrastructure;

namespace SareeBilling.Tests;

/// <summary>
/// Wires the real Application + Infrastructure services exactly like the app does,
/// but against a throw-away database in the temp folder.
/// </summary>
public sealed class TestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string _root;

    private TestHost(string root, ServiceProvider provider)
    {
        _root = root;
        _provider = provider;
    }

    public IServiceProvider Services => _provider;

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public static async Task<TestHost> StartAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "sareebilling-tests", Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(new TestPaths(root))
            .AddApplication();

        var host = new TestHost(root, services.BuildServiceProvider());
        await host.Get<IDatabaseInitializer>().InitializeAsync();
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: temp files are cleaned up by Windows eventually.
        }
    }

    private sealed class TestPaths(string root) : IAppPaths
    {
        public string RootDirectory => root;
        public string DataDirectory => Path.Combine(root, "Data");
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string AutoBackupDirectory => Path.Combine(root, "AutoBackups");
        public string LogDirectory => Path.Combine(root, "Logs");
    }
}
