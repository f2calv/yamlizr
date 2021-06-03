using CasCap.Models;
using CasCap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Xunit.Abstractions;
namespace CasCap.Apis.AzureDevOps.Tests
{
    public abstract class TestBase
    {
        protected IApiService _apiSvc;

        public TestBase(ITestOutputHelper output)
        {
            var PAT = Guid.NewGuid().ToString();//generate random PAT

            var initialData = new Dictionary<string, string>
            {
                { $"{nameof(CasCap)}:{nameof(AzureDevOpsOptions)}:{nameof(AzureDevOpsOptions.PAT)}", PAT}
            };

            var configuration = new ConfigurationBuilder()
                //.AddCasCapConfiguration()
                .AddJsonFile($"appsettings.Test.json", optional: true, reloadOnChange: false)
                .AddInMemoryCollection(initialData)
                .Build();

            //initiate ServiceCollection w/logging
            var services = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddXUnitLogging(output);

            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            //add services
            services.AddSingleton<IApiService>(s => new ApiService(loggerFactory.CreateLogger<ApiService>(), PAT));

            //assign services to be tested
            serviceProvider = services.BuildServiceProvider();
            _apiSvc = serviceProvider.GetRequiredService<IApiService>();
        }
    }
}