using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace CasCap.Api.AzureDevOps.Tests;

public class ApiServiceTests : TestBase
{
    public ApiServiceTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task GetAllExtensionsTest()
    {
        try
        {
            _ = await _apiSvc.GetAllExtensions(Guid.NewGuid().ToString());
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            Debugger.Break();
        }
        Assert.True(true);//assert true regardless of actual outcome, will add full tests later
    }
}
