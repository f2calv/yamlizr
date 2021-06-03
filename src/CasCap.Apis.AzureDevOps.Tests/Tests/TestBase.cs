using CasCap.Models;
using CasCap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
namespace CasCap.Apis.AzureDevOps.Tests
{
    public abstract class TestBase
    {
        protected IApiService _apiSvc;

        public TestBase(ITestOutputHelper output)
        {
            var configuration = new ConfigurationBuilder()
                //.AddCasCapConfiguration()
                .AddJsonFile($"appsettings.Test.json", optional: false, reloadOnChange: false)
                .Build();

            //initiate ServiceCollection w/logging
            var services = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddXUnitLogging(output);

            //add services
            var section = configuration.GetSection(AzureDevOpsOptions.sectionKey);
            var azureDevOpsOptions = section.Get<AzureDevOpsOptions>();
            services.Configure<AzureDevOpsOptions>(section);
            services.AddSingleton<IApiService>(s => new ApiService(azureDevOpsOptions.PAT));

            //assign services to be tested
            var serviceProvider = services.BuildServiceProvider();
            _apiSvc = serviceProvider.GetRequiredService<IApiService>();
        }
    }
}