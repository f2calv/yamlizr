using CasCap.Models;

namespace CasCap.Api.AzureDevOps.Tests.Integration;

/// <summary>
/// Submits YAML to the Azure DevOps pipeline preview endpoint, which parses it and expands its
/// templates without queueing anything.
/// </summary>
/// <remarks>
/// A schema check proves a document is well formed; this proves Azure DevOps will accept it, which
/// is the only claim that matters for generated output. See issue #366.
/// </remarks>
[Trait("Category", "Integration")]
public class PipelineValidationTests : TestBase
{
    /// <summary>Initialises the shared Azure DevOps connection.</summary>
    /// <param name="output">xUnit sink that test logging is written to.</param>
    public PipelineValidationTests(ITestOutputHelper output) : base(output) { }

    private const string NoValidationPipeline =
        "No validation pipeline configured, set CasCap:AzureDevOpsOptions:ValidationPipelineId to the id of yamlizr.test.validation.";

    private bool CanValidate => IsConfigured
        && Options.ValidationPipelineId.HasValue
        && !string.IsNullOrWhiteSpace(Options.OrganisationUri)
        && !string.IsNullOrWhiteSpace(Options.Project);

    [Fact]
    public async Task WellFormedYaml_IsAccepted()
    {
        Assert.SkipUnless(CanValidate, NoValidationPipeline);

        var result = await Validate("""
            pool:
              vmImage: ubuntu-latest
            steps:
            - script: echo hello
              displayName: Say hello
            """);

        Assert.True(result.IsValid, result.Message);
        Assert.NotEmpty(result.FinalYaml);
    }

    //the generator emitted exactly this until the identifier sanitiser was added
    [Fact]
    public async Task JobNameWithAnIllegalCharacter_IsRejected()
    {
        Assert.SkipUnless(CanValidate, NoValidationPipeline);

        var result = await Validate("""
            pool:
              vmImage: ubuntu-latest
            jobs:
            - job: Phase three, fan-in
              steps:
              - script: echo hello
            """);

        Assert.False(result.IsValid);
        Assert.Contains("invalid name", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    //the generator emitted exactly this until template parameters became a sequence
    [Fact]
    public async Task TemplateParametersAsAMapping_AreRejected()
    {
        Assert.SkipUnless(CanValidate, NoValidationPipeline);

        var result = await Validate("""
            parameters:
              greeting: hello
            steps:
            - script: echo ${{ parameters.greeting }}
            """);

        Assert.False(result.IsValid);
        Assert.Contains("mapping was not expected", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeneratedIdentifiers_AreAccepted()
    {
        Assert.SkipUnless(CanValidate, NoValidationPipeline);

        //mirrors the shape yamlizr now emits for a multi-phase build, including a fan-in
        var result = await Validate("""
            pool:
              vmImage: ubuntu-latest
            jobs:
            - job: Phase_one_0
              displayName: Phase one
              steps:
              - script: echo one
            - job: Phase_two_1
              displayName: Phase two
              dependsOn:
              - Phase_one_0
              steps:
              - script: echo two
            - job: Phase_three_fan_in_2
              displayName: Phase three, fan-in
              dependsOn:
              - Phase_one_0
              - Phase_two_1
              condition: succeededOrFailed()
              steps:
              - script: echo three
            """);

        Assert.True(result.IsValid, result.Message);
    }

    private Task<PipelineValidationResult> Validate(string yaml)
        => _apiSvc.Validate(Options.OrganisationUri, Options.Project, Options.ValidationPipelineId.Value, yaml, TestContext.Current.CancellationToken);
}
