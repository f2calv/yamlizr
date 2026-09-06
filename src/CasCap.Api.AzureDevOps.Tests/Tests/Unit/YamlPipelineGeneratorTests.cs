using CasCap.Common.Exceptions;
using CasCap.Models;
using CasCap.Utilities;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using System.Collections.Concurrent;

namespace CasCap.Api.AzureDevOps.Tests.Unit;

/// <summary>Offline tests for <see cref="YamlPipelineGenerator"/>.</summary>
[Trait("Category", "Generation")]
public class YamlPipelineGeneratorTests
{
    [Fact]
    public void GenPipeline_InstalledTask_EmitsStep()
    {
        var generator = YamlizrTestData.Generator(
            YamlizrTestData.BuildDefinition(YamlizrTestData.Step(YamlizrTestData.KnownTaskId)));

        var pipeline = generator.GenPipeline();

        Assert.NotNull(pipeline);
        var step = Assert.Single(pipeline.steps);
        Assert.Equal("SampleTask@1", step.task);
        Assert.Equal("sample step", step.displayName);
        Assert.Empty(generator.Warnings);
    }

    //regression test for https://github.com/f2calv/yamlizr/issues/177
    [Fact]
    public void GenPipeline_TaskNeitherInstalledNorATaskGroup_WarnsInsteadOfThrowing()
    {
        var generator = YamlizrTestData.Generator(
            YamlizrTestData.BuildDefinition(YamlizrTestData.Step(YamlizrTestData.UnknownTaskId, displayName: "removed extension")));

        var pipeline = generator.GenPipeline();

        Assert.Null(pipeline);
        var warning = Assert.Single(generator.Warnings);
        Assert.Contains("removed extension", warning);
        Assert.Contains(YamlizrTestData.UnknownTaskId.ToString(), warning);
    }

    //regression test for https://github.com/f2calv/yamlizr/issues/177
    [Fact]
    public void GenPipeline_MixOfResolvableAndUnresolvableTasks_KeepsTheResolvableStep()
    {
        var generator = YamlizrTestData.Generator(
            YamlizrTestData.BuildDefinition(
                YamlizrTestData.Step(YamlizrTestData.KnownTaskId, displayName: "kept"),
                YamlizrTestData.Step(YamlizrTestData.UnknownTaskId, displayName: "dropped")));

        var pipeline = generator.GenPipeline();

        var step = Assert.Single(pipeline.steps);
        Assert.Equal("kept", step.displayName);
        Assert.Single(generator.Warnings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void GenPipeline_UnusableVersionSpec_WarnsInsteadOfThrowing(string versionSpec)
    {
        var generator = YamlizrTestData.Generator(
            YamlizrTestData.BuildDefinition(YamlizrTestData.Step(YamlizrTestData.KnownTaskId, versionSpec)));

        var pipeline = generator.GenPipeline();

        Assert.Null(pipeline);
        Assert.Contains(generator.Warnings, w => w.Contains("unusable version"));
    }

    [Theory]
    [InlineData("1.*", "SampleTask@1")]
    [InlineData("1.2.3", "SampleTask@1")]
    [InlineData("1.0", "SampleTask@1")]
    public void GenPipeline_VersionSpecForms_ResolveToTheMajorVersion(string versionSpec, string expected)
    {
        var generator = YamlizrTestData.Generator(
            YamlizrTestData.BuildDefinition(YamlizrTestData.Step(YamlizrTestData.KnownTaskId, versionSpec)));

        var pipeline = generator.GenPipeline();

        Assert.Equal(expected, Assert.Single(pipeline.steps).task);
    }

    [Fact]
    public void GenPipeline_DisabledStep_IsNotEmitted()
    {
        var step = YamlizrTestData.Step(YamlizrTestData.KnownTaskId);
        step.Enabled = false;

        var pipeline = YamlizrTestData.Generator(YamlizrTestData.BuildDefinition(step)).GenPipeline();

        Assert.Null(pipeline);
    }

    //regression test for https://github.com/f2calv/yamlizr/issues/177, reported in PR #260
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GenPipeline_PhaseWithoutAName_IsGivenAGeneratedName(string phaseName)
    {
        var generator = YamlizrTestData.Generator(
            YamlizrTestData.BuildDefinition(phaseName, YamlizrTestData.Step(YamlizrTestData.KnownTaskId)));

        var pipeline = generator.GenPipeline();

        //a single job is flattened to steps, so the generated name is only observable as no throw
        Assert.NotNull(pipeline);
        Assert.Single(pipeline.steps);
    }

    [Fact]
    public void GenPipeline_MultipleEnvironments_NamesEachStageAfterItsEnvironment()
    {
        var generator = YamlizrTestData.Generator(YamlizrTestData.ReleaseDefinition("Dev", "Test", "Prod"));

        var pipeline = generator.GenPipeline();

        Assert.NotNull(pipeline.stages);
        Assert.Equal(["Dev", "Test", "Prod"], pipeline.stages.Select(p => p.stage));
        //the release definition names the document, so repeating it on every stage loses the environment
        Assert.Equal(["Dev", "Test", "Prod"], pipeline.stages.Select(p => p.displayName));
    }

    [Fact]
    public void GenPipeline_EnvironmentWithoutAName_FallsBackToTheStageIdentifier()
    {
        var generator = YamlizrTestData.Generator(YamlizrTestData.ReleaseDefinition("Dev", "   "));

        var pipeline = generator.GenPipeline();

        var unnamed = Assert.Single(pipeline.stages, p => p.stage == "Stage_2");
        Assert.Equal("Stage_2", unnamed.displayName);
    }

    //a job identifier must match [A-Za-z_][A-Za-z0-9_]*, and Azure DevOps rejects the pipeline otherwise
    [Theory]
    [InlineData("Phase three, fan-in")]
    [InlineData("build & test")]
    [InlineData("  leading and trailing  ")]
    [InlineData("1st phase")]
    public void GenPipeline_PhaseNameNeedingSanitising_ProducesAValidJobIdentifier(string phaseName)
    {
        var definition = YamlizrTestData.BuildDefinitionWithPhases(
            "Azure Pipelines", YamlizrTestData.Step(YamlizrTestData.KnownTaskId), "Phase one", phaseName);

        var pipeline = YamlizrTestData.Generator(definition).GenPipeline();

        Assert.NotNull(pipeline.jobs);
        //asserts the contract rather than an exact name, which also carries the issue #368 index suffix
        Assert.All(pipeline.jobs, p => Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", p.job));
    }

    [Fact]
    public void GenPipeline_NeitherBuildNorRelease_IsRejected()
    {
        var generator = new YamlPipelineGenerator(
            null,
            null,
            new Dictionary<Guid, Dictionary<int, TaskObj>>(),
            new Dictionary<TaskGroupVersion, TaskGroup>(),
            new ConcurrentDictionary<TaskGroupVersion, Template>(),
            new Dictionary<int, VariableGroup>(),
            inlineTaskGroups: false,
            DeployPhaseTypes.AgentBasedDeployment);

        Assert.Throws<GenericException>(generator.GenPipeline);
    }
}
