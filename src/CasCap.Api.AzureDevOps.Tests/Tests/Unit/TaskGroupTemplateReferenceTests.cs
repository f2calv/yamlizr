using CasCap.Models;
using CasCap.Utilities;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using System.Collections.Concurrent;

namespace CasCap.Api.AzureDevOps.Tests.Unit;

/// <summary>Task-group template references must resolve from the folder the pipeline is written to.</summary>
[Trait("Category", "Generation")]
public class TaskGroupTemplateReferenceTests
{
    private static YamlPipelineGenerator Generator(BuildDefinition? build, ReleaseDefinition? release, int folderDepth)
    {
        var taskGroup = new TaskGroup { Id = YamlizrTestData.KnownTaskId, Name = "Group", Version = new Microsoft.TeamFoundation.DistributedTask.WebApi.TaskVersion { Major = 1 } };
        return new YamlPipelineGenerator(
            build,
            release,
            [],
            new Dictionary<TaskGroupVersion, TaskGroup> { [new TaskGroupVersion(taskGroup.Id, 1)] = taskGroup },
            new ConcurrentDictionary<TaskGroupVersion, Template>(),
            [],
            inlineTaskGroups: false,
            DeployPhaseTypes.AgentBasedDeployment,
            folderDepth);
    }

    [Theory]
    [InlineData(0, "../AzureDevOpsTaskGroups/Group-v1.yml")]
    [InlineData(1, "../../AzureDevOpsTaskGroups/Group-v1.yml")]
    [InlineData(2, "../../../AzureDevOpsTaskGroups/Group-v1.yml")]
    public void Build_TemplateReference_IsRelativeToFolderDepth(int depth, string expected)
    {
        var build = YamlizrTestData.BuildDefinition(YamlizrTestData.Step(YamlizrTestData.KnownTaskId));

        var pipeline = Generator(build, null, depth).GenPipeline();

        Assert.NotNull(pipeline?.Steps);
        Assert.Equal(expected, pipeline.Steps[0].template);
    }

    [Theory]
    [InlineData(0, "../AzureDevOpsTaskGroups/Group-v1.yml")]
    [InlineData(1, "../../AzureDevOpsTaskGroups/Group-v1.yml")]
    [InlineData(2, "../../../AzureDevOpsTaskGroups/Group-v1.yml")]
    public void Release_TemplateReference_IsRelativeToFolderDepth(int depth, string expected)
    {
        var release = YamlizrTestData.ReleaseDefinition("Prod");

        var pipeline = Generator(null, release, depth).GenPipeline();

        Assert.NotNull(pipeline?.Steps);
        Assert.Equal(expected, pipeline.Steps[0].template);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("\\", 0)]
    [InlineData("\\Team", 1)]
    [InlineData("\\Team\\App", 2)]
    [InlineData("/Team/App/Sub/", 3)]
    public void GetSubFolders_CountsSegments(string? folder, int expected)
        => Assert.Equal(expected, FolderMatcher.GetSubFolders(folder).Length);
}
