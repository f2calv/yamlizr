using CasCap.Models;
using CasCap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            var initialData = new Dictionary<string, string>
            {
                { $"{nameof(CasCap)}:{nameof(AzureDevOpsOptions)}:{nameof(AzureDevOpsOptions.PAT)}", Guid.NewGuid().ToString() },//generate random PAT
            };

            var configuration = new ConfigurationBuilder()
                //.AddCasCapConfiguration()
                //.AddJsonFile($"appsettings.Test.json", optional: false, reloadOnChange: false)
                .AddInMemoryCollection(initialData)
                .Build();

            //initiate ServiceCollection w/logging
            var services = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddXUnitLogging(output);

            //add services
            var section = configuration.GetSection(AzureDevOpsOptions.sectionKey);
            var azureDevOpsOptions = section.Get<AzureDevOpsOptions>();
            services.Configure<AzureDevOpsOptions>(section);
            services.AddSingleton(s => azureDevOpsOptions);
            services.AddSingleton<IApiService>();

            //assign services to be tested
            var serviceProvider = services.BuildServiceProvider();
            _apiSvc = serviceProvider.GetRequiredService<IApiService>();
        }
    }
}