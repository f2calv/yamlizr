using CasCap.Models;
using CasCap.Utilities;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using System.Collections.Concurrent;
using TaskDefinitionReference = Microsoft.TeamFoundation.DistributedTask.WebApi.TaskDefinitionReference;

namespace CasCap.Api.AzureDevOps.Tests.Unit;

/// <summary>Task-group template references must resolve from the folder the pipeline is written to.</summary>
[Trait("Category", "Generation")]
public class TaskGroupTemplateReferenceTests
{
    private static readonly Guid ChildTaskGroupId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ParentTaskGroupId = new("33333333-3333-3333-3333-333333333333");

    //a template always sits directly in AzureDevOpsTaskGroups, so this reference never varies
    private const string ChildReference = "../AzureDevOpsTaskGroups/Child-v1.yml";

    private static YamlPipelineGenerator Generator(BuildDefinition? build, ReleaseDefinition? release, int folderDepth,
        Dictionary<TaskGroupVersion, TaskGroup>? taskGroups = null, ConcurrentDictionary<TaskGroupVersion, Template>? templates = null,
        Dictionary<Guid, Dictionary<int, TaskObj>>? taskMap = null)
    {
        var taskGroup = new TaskGroup { Id = YamlizrTestData.KnownTaskId, Name = "Group", Version = new Microsoft.TeamFoundation.DistributedTask.WebApi.TaskVersion { Major = 1 } };
        return new YamlPipelineGenerator(
            build,
            release,
            taskMap ?? [],
            taskGroups ?? new Dictionary<TaskGroupVersion, TaskGroup> { [new TaskGroupVersion(taskGroup.Id, 1)] = taskGroup },
            templates ?? new ConcurrentDictionary<TaskGroupVersion, Template>(),
            [],
            inlineTaskGroups: false,
            DeployPhaseTypes.AgentBasedDeployment,
            folderDepth);
    }

    private static string Reference(int depth, string filename)
        => $"{string.Concat(Enumerable.Repeat("../", 1 + depth))}AzureDevOpsTaskGroups/{filename}";

    private static Template TemplateFor(ConcurrentDictionary<TaskGroupVersion, Template> templates, Guid taskGroupId)
        => templates[new TaskGroupVersion(taskGroupId, 1)];

    //a task group whose steps use another task group, which is what produces a template-to-template reference
    private static Dictionary<TaskGroupVersion, TaskGroup> NestedTaskGroups()
    {
        var child = TaskGroup(ChildTaskGroupId, "Child", YamlizrTestData.KnownTaskId);
        var parent = TaskGroup(ParentTaskGroupId, "Parent", ChildTaskGroupId);
        return new()
        {
            [new TaskGroupVersion(child.Id, 1)] = child,
            [new TaskGroupVersion(parent.Id, 1)] = parent,
        };

        static TaskGroup TaskGroup(Guid id, string name, Guid stepTaskId) => new()
        {
            Id = id,
            Name = name,
            Version = new Microsoft.TeamFoundation.DistributedTask.WebApi.TaskVersion { Major = 1 },
            Tasks =
            [
                new TaskGroupStep
                {
                    Enabled = true,
                    DisplayName = $"{name} step",
                    Task = new TaskDefinitionReference { Id = stepTaskId, VersionSpec = "1.*" },
                },
            ],
        };
    }

    private static ReleaseDefinition ReleaseWithParentTaskGroup()
    {
        var definition = new ReleaseDefinition { Id = 8, Name = "sample release", Environments = [] };
        definition.Environments.Add(new ReleaseDefinitionEnvironment
        {
            Name = "Prod",
            Rank = 1,
            DeployPhases =
            [
                new AgentBasedDeployPhase
                {
                    Name = "Agent job",
                    Rank = 1,
                    DeploymentInput = new AgentDeploymentInput { Condition = "succeeded()" },
                    WorkflowTasks =
                    [
                        new WorkflowTask
                        {
                            Enabled = true,
                            Name = "parent group step",
                            TaskId = ParentTaskGroupId,
                            Version = "1.*",
                        },
                    ],
                },
            ],
            Variables = new Dictionary<string, ConfigurationVariableValue>(),
            VariableGroups = [],
        });
        return definition;
    }

    [Theory]
    [InlineData(0, "../AzureDevOpsTaskGroups/Group-v1.yml")]
    [InlineData(1, "../../AzureDevOpsTaskGroups/Group-v1.yml")]
    [InlineData(2, "../../../AzureDevOpsTaskGroups/Group-v1.yml")]
    public void Build_TemplateReference_IsRelativeToFolderDepth(int depth, string expected)
    {
        var build = YamlizrTestData.BuildDefinition(YamlizrTestData.Step(YamlizrTestData.KnownTaskId));

        var pipeline = Generator(build, null, depth).GenPipeline();

        Assert.NotNull(pipeline);
        Assert.NotNull(pipeline.Steps);
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

        Assert.NotNull(pipeline);
        Assert.NotNull(pipeline.Steps);
        Assert.Equal(expected, pipeline.Steps[0].template);
    }

    //regression test for the second review on PR #401: a template's own references must not inherit
    //the folder depth of the pipeline that happens to create the template first
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Build_NestedTaskGroups_ChildTemplateReference_IgnoresFolderDepth(int depth)
    {
        var templates = new ConcurrentDictionary<TaskGroupVersion, Template>();
        var build = YamlizrTestData.BuildDefinition(YamlizrTestData.Step(ParentTaskGroupId));

        var pipeline = Generator(build, null, depth, NestedTaskGroups(), templates, YamlizrTestData.TaskMap()).GenPipeline();

        Assert.NotNull(pipeline);
        Assert.NotNull(pipeline.Steps);
        Assert.Equal(Reference(depth, "Parent-v1.yml"), Assert.Single(pipeline.Steps).template);
        Assert.Equal(ChildReference, Assert.Single(TemplateFor(templates, ParentTaskGroupId).Steps!).template);
        Assert.Equal("SampleTask@1", Assert.Single(TemplateFor(templates, ChildTaskGroupId).Steps!).task);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Release_NestedTaskGroups_ChildTemplateReference_IgnoresFolderDepth(int depth)
    {
        var templates = new ConcurrentDictionary<TaskGroupVersion, Template>();

        var pipeline = Generator(null, ReleaseWithParentTaskGroup(), depth, NestedTaskGroups(), templates, YamlizrTestData.TaskMap()).GenPipeline();

        Assert.NotNull(pipeline);
        Assert.NotNull(pipeline.Steps);
        Assert.Equal(Reference(depth, "Parent-v1.yml"), Assert.Single(pipeline.Steps).template);
        Assert.Equal(ChildReference, Assert.Single(TemplateFor(templates, ParentTaskGroupId).Steps!).template);
    }

    //the body is built once by whichever pipeline reaches it first, so generating the deep release
    //before the default-layout build is what used to bake the release's depth into the shared template
    [Fact]
    public void SharedNestedTaskGroups_DeepReleaseThenDefaultBuild_ProduceStableReferences()
    {
        var taskGroups = NestedTaskGroups();
        var templates = new ConcurrentDictionary<TaskGroupVersion, Template>();

        var release = Generator(null, ReleaseWithParentTaskGroup(), 2, taskGroups, templates, YamlizrTestData.TaskMap()).GenPipeline();
        var build = Generator(YamlizrTestData.BuildDefinition(YamlizrTestData.Step(ParentTaskGroupId)), null, 0, taskGroups, templates, YamlizrTestData.TaskMap()).GenPipeline();

        Assert.NotNull(release);
        Assert.NotNull(release.Steps);
        Assert.Equal(Reference(2, "Parent-v1.yml"), Assert.Single(release.Steps).template);
        Assert.NotNull(build);
        Assert.NotNull(build.Steps);
        Assert.Equal(Reference(0, "Parent-v1.yml"), Assert.Single(build.Steps).template);
        Assert.Equal(ChildReference, Assert.Single(TemplateFor(templates, ParentTaskGroupId).Steps!).template);
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
