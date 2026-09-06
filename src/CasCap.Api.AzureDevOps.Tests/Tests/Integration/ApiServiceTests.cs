namespace CasCap.Api.AzureDevOps.Tests.Integration;

/// <summary>Read-only tests against a live Azure DevOps organisation.</summary>
[Trait("Category", "Integration")]
public class ApiServiceTests : TestBase
{
    /// <summary>Initialises the shared Azure DevOps connection.</summary>
    /// <param name="output">xUnit sink that test logging is written to.</param>
    public ApiServiceTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task GetAllExtensions()
    {
        Assert.SkipUnless(IsConfigured, NotConfigured);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Options.OrganisationUri),
            "No organisation configured, set CasCap:AzureDevOpsOptions:OrganisationUri.");

        var extensions = await _apiSvc.GetAllExtensions(Options.OrganisationUri.TrimEnd('/'));

        Assert.NotNull(extensions);
        Assert.NotEmpty(extensions);
        //every installed task must be identifiable, the generator maps steps by id plus major version
        Assert.All(extensions, extension =>
        {
            Assert.NotEqual(Guid.Empty, extension.id);
            Assert.False(string.IsNullOrWhiteSpace(extension.name));
            Assert.NotNull(extension.version);
        });
    }
}
