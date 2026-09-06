using CasCap.Models;
using CasCap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CasCap.Api.AzureDevOps.Tests.Integration;

/// <summary>
/// Shared setup for tests which call a live Azure DevOps organisation.
/// </summary>
/// <remarks>
/// Configuration is read in repository order: <c>appsettings.Test.json</c>, then User Secrets for local
/// runs, then environment variables for CI. Set the token locally with
/// <c>dotnet user-secrets set CasCap:AzureDevOpsOptions:PAT "your-token"</c>.
/// </remarks>
public abstract class TestBase : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    /// <summary>Azure DevOps connection details resolved from configuration.</summary>
    protected AzureDevOpsOptions Options { get; }

    /// <summary>Azure DevOps REST client under test, null when no token is configured.</summary>
    protected IApiService _apiSvc { get; }

    /// <summary>True when a token is configured, so a live test can run.</summary>
    protected bool IsConfigured => !string.IsNullOrWhiteSpace(Options.PAT);

    /// <summary>Reason reported by every skipped live test.</summary>
    protected const string NotConfigured =
        "No Azure DevOps token configured, set CasCap:AzureDevOpsOptions:PAT via user secrets or CasCap__AzureDevOpsOptions__PAT.";

    /// <summary>Builds configuration and, when a token is available, the client under test.</summary>
    /// <param name="output">xUnit sink that test logging is written to.</param>
    protected TestBase(ITestOutputHelper output)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: true, reloadOnChange: false)
            .AddUserSecrets<TestBase>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        Options = configuration.GetSection(AzureDevOpsOptions.ConfigurationSectionName).Get<AzureDevOpsOptions>()
            ?? new AzureDevOpsOptions();

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddXUnitLogging(output);

        _serviceProvider = services.BuildServiceProvider();

        if (IsConfigured)
        {
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            _apiSvc = new ApiService(loggerFactory.CreateLogger<ApiService>(), Options.PAT);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
