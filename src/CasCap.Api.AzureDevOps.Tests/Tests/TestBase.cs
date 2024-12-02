using CasCap.Models;
using CasCap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace CasCap.Apis.AzureDevOps.Tests;

public abstract class TestBase
{
    protected IApiService _apiSvc;

    public TestBase(ITestOutputHelper output)
    {
        //dotnet user-secrets set CasCap:AzureDevOpsOptions:PAT "xxxxx" <-- test PAT here

        var configuration = new ConfigurationBuilder()
            //.AddCasCapConfiguration()
            .AddJsonFile($"appsettings.Test.json", optional: true, reloadOnChange: false)
            .AddUserSecrets<TestBase>()
            .Build();

        var pat = configuration[$"{nameof(CasCap)}:{nameof(AzureDevOpsOptions)}:{nameof(AzureDevOpsOptions.PAT)}"];
        //if (string.IsNullOrWhiteSpace(pat)) throw new NotSupportedException("cannot find Azure DevOps PAT");

        //initiate ServiceCollection w/logging
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddXUnitLogging(output);

        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        //add services
        services.AddSingleton<IApiService>(s => new ApiService(loggerFactory.CreateLogger<ApiService>(), pat));

        //assign services to be tested
        serviceProvider = services.BuildServiceProvider();
        _apiSvc = serviceProvider.GetRequiredService<IApiService>();
    }
}
