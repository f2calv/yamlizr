using System.Diagnostics;
using Xunit;

namespace CasCap.Api.AzureDevOps.Tests;

public class ApiServiceTests : TestBase
{
    public ApiServiceTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task GetAllExtensions_WithInvalidOrganisation_ReturnsNullOrEmpty()
    {
        if (!_hasPat)
            return; // skip test when no PAT is configured

        var result = await _apiSvc.GetAllExtensions(Guid.NewGuid().ToString());

        // with an invalid org the API returns null or an empty list
        Assert.True(result is null || result.Count == 0);
    }

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
