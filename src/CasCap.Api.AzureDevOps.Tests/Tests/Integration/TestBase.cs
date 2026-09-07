using CasCap.Abstractions;
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

    /// <summary>Azure DevOps REST client under test, absent when no token is configured.</summary>
    /// <remarks>Reading this from a test that did not skip on <see cref="IsConfigured"/> is a bug in the test.</remarks>
    protected IApiService ApiSvc => _apiSvc ?? throw new InvalidOperationException(NotConfigured);
    private readonly IApiService? _apiSvc;

    /// <summary>True when a token is configured, so a live test can run.</summary>
    protected bool IsConfigured => !string.IsNullOrWhiteSpace(Options.PAT);

    //Each of these is established by the guard a live test skips on, and reading them through an
    //accessor is what carries that guarantee from the guard to the call site.

    /// <summary>Configured access token.</summary>
    protected string Token => Options.PAT ?? throw new InvalidOperationException(NotConfigured);

    /// <summary>Configured organisation Uri, with any trailing separator removed.</summary>
    protected string OrganisationUri => Options.OrganisationUri?.TrimEnd('/') ?? throw new InvalidOperationException(NotConfigured);

    /// <summary>Configured team project name.</summary>
    protected string ProjectName => Options.Project ?? throw new InvalidOperationException(NotConfigured);

    /// <summary>Configured identifier of the pipeline that generated YAML is validated against.</summary>
    protected int ValidationPipelineId => Options.ValidationPipelineId ?? throw new InvalidOperationException(NotConfigured);

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

        //the token is what IsConfigured tests, so matching on it here is what tells the compiler
        if (Options.PAT is { } pat && !string.IsNullOrWhiteSpace(pat))
        {
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            _apiSvc = new ApiService(loggerFactory.CreateLogger<ApiService>(), pat);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
