namespace CasCap.Api.AzureDevOps.Tests.Integration;

/// <summary>Read-only tests against a live Azure DevOps organisation.</summary>
/// <remarks>Initialises the shared Azure DevOps connection.</remarks>
/// <param name="output">xUnit sink that test logging is written to.</param>
[Trait("Category", "Integration")]
public class ApiServiceTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task GetAllExtensions()
    {
        Assert.SkipUnless(IsConfigured, NotConfigured);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Options.OrganisationUri),
            "No organisation configured, set CasCap:AzureDevOpsOptions:OrganisationUri.");

        var extensions = await ApiSvc.GetAllExtensions(
            Options.OrganisationUri.TrimEnd('/'), TestContext.Current.CancellationToken);

        Assert.NotNull(extensions);
        Assert.NotEmpty(extensions);
        //every installed task must be identifiable, the generator maps steps by id plus major version
        Assert.All(extensions, extension =>
        {
            Assert.NotEqual(Guid.Empty, extension.Id);
            Assert.False(string.IsNullOrWhiteSpace(extension.Name));
            Assert.NotNull(extension.Version);
        });
    }
}
