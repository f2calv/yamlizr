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

    /// <summary>
    /// Converts a definition whose phase names contain characters that are illegal in a YAML
    /// identifier, then asks Azure DevOps to parse the result.
    /// </summary>
    /// <remarks>
    /// The only test that runs the generator and validates what it actually produced. The samples
    /// above would still pass if the generator regressed, because they are hand written; this one
    /// fails, which is what makes it a regression test rather than a demonstration.
    /// </remarks>
    [Fact]
    public async Task GeneratedYaml_ForPhaseNamesNeedingSanitising_IsAccepted()
    {
        Assert.SkipUnless(CanValidate, NoValidationPipeline);

        await AssertGeneratedYamlIsAccepted("Phase one", "Phase two", "Phase three, fan-in");
    }

    //https://github.com/f2calv/yamlizr/issues/368
    [Fact]
    public async Task GeneratedYaml_ForPhasesSharingOneName_IsAccepted()
    {
        Assert.SkipUnless(CanValidate, NoValidationPipeline);

        await AssertGeneratedYamlIsAccepted("Agent job", "Agent job", "Agent job");
    }

    private async Task AssertGeneratedYamlIsAccepted(params string[] phaseNames)
    {
        //CmdLine really exists, because Azure DevOps rejects a reference to a task it cannot resolve
        var taskMap = YamlizrTestData.TaskMap("CmdLine", 2, "script");
        var step = YamlizrTestData.Step(
            YamlizrTestData.KnownTaskId,
            versionSpec: "2.*",
            inputs: new Dictionary<string, string> { ["script"] = "echo hello" });

        var definition = YamlizrTestData.BuildDefinitionWithPhases("Azure Pipelines", step, phaseNames);
        var yaml = YamlizrTestData.Generator(definition, taskMap).GenPipeline().ToString();

        var result = await Validate(yaml);

        Assert.True(result.IsValid, $"{result.Message}{Environment.NewLine}{yaml}");
    }

    private Task<PipelineValidationResult> Validate(string yaml)
        => _apiSvc.Validate(Options.OrganisationUri, Options.Project, Options.ValidationPipelineId.Value, yaml, TestContext.Current.CancellationToken);
}
