using CasCap.Common.Exceptions;
using CasCap.Models;
using CasCap.Utilities;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using System.Collections.Concurrent;
using Xunit;

namespace CasCap.Api.AzureDevOps.Tests;

public class YamlPipelineGeneratorTests
{
    [Fact]
    public void GenPipeline_WithBothBuildAndRelease_ThrowsGenericException()
    {
        var generator = CreateGenerator(new BuildDefinition(), new ReleaseDefinition());

        Assert.Throws<GenericException>(() => generator.GenPipeline());
    }

    [Fact]
    public void GenPipeline_WithNeitherBuildNorRelease_ThrowsGenericException()
    {
        var generator = CreateGenerator(null, null);

        Assert.Throws<GenericException>(() => generator.GenPipeline());
    }

    [Fact]
    public void GenPipeline_BuildWithNoMatchingPhases_ReturnsNull()
    {
        // DesignerProcess with no phases → nothing to generate
        var build = new BuildDefinition
        {
            Process = new DesignerProcess() // empty phases list by default
        };

        var generator = CreateGenerator(build, null);
        var result = generator.GenPipeline();

        Assert.Null(result);
    }

    [Fact]
    public void GenPipeline_ReleaseWithNoEnvironments_ReturnsNull()
    {
        // ReleaseDefinition.Environments is null by default → nothing to generate
        var release = new ReleaseDefinition();

        var generator = CreateGenerator(null, release);
        var result = generator.GenPipeline();

        Assert.Null(result);
    }

    [Fact]
    public void GenPipeline_BuildWithValidPhaseAndKnownTask_GeneratesPipeline()
    {
        var taskId = Guid.NewGuid();
        const int taskMajorVersion = 2;
        const string taskName = "DotNetCoreCLI";

        var taskObj = new TaskObj
        {
            id = taskId,
            name = taskName,
            version = new CasCap.Models.TaskVersion { major = taskMajorVersion, minor = 0, patch = 0 },
            contributionIdentifier = null,
            inputs = new List<TaskInput>(),
            inputMap = new Dictionary<string, TaskInput>()
        };

        var taskMap = new Dictionary<Guid, Dictionary<int, TaskObj>>
        {
            { taskId, new Dictionary<int, TaskObj> { { taskMajorVersion, taskObj } } }
        };

        var step = new BuildDefinitionStep
        {
            Enabled = true,
            DisplayName = "Build project",
            Condition = "succeeded()",
            ContinueOnError = false,
            TimeoutInMinutes = 0,
            TaskDefinition = new Microsoft.TeamFoundation.Build.WebApi.TaskDefinitionReference
            {
                Id = taskId,
                VersionSpec = $"{taskMajorVersion}.*"
            }
            // Inputs and Environment are initialized to empty collections by default
        };

        var phase = new Phase
        {
            Name = "Build",
            Target = new AgentPoolQueueTarget(), // Type == 1, matches the filter in GenBuildStage
            JobCancelTimeoutInMinutes = 0,
            JobTimeoutInMinutes = 0
        };
        phase.Steps.Add(step);

        var process = new DesignerProcess();
        process.Phases.Add(phase); // Phases is a read-only property returning the initialized list

        var build = new BuildDefinition
        {
            Name = "My-Build",
            Process = process
        };

        var generator = new YamlPipelineGenerator(
            build,
            null,
            taskMap,
            new Dictionary<TaskGroupVersion, TaskGroup>(),
            new ConcurrentDictionary<TaskGroupVersion, Template>(),
            new Dictionary<int, Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup>(),
            false,
            DeployPhaseTypes.AgentBasedDeployment
        );

        var pipeline = generator.GenPipeline();

        Assert.NotNull(pipeline);
        var yaml = pipeline.ToString();
        Assert.Contains($"{taskName}@{taskMajorVersion}", yaml);
    }

    [Fact]
    public void GenPipeline_BuildWithDisabledStep_ReturnsNull()
    {
        var taskId = Guid.NewGuid();
        var taskObj = new TaskObj
        {
            id = taskId,
            name = "DotNetCoreCLI",
            version = new CasCap.Models.TaskVersion { major = 2, minor = 0, patch = 0 },
            inputs = new List<TaskInput>(),
            inputMap = new Dictionary<string, TaskInput>()
        };

        var taskMap = new Dictionary<Guid, Dictionary<int, TaskObj>>
        {
            { taskId, new Dictionary<int, TaskObj> { { 2, taskObj } } }
        };

        var step = new BuildDefinitionStep
        {
            Enabled = false, // disabled step should be excluded
            DisplayName = "Disabled step",
            TaskDefinition = new Microsoft.TeamFoundation.Build.WebApi.TaskDefinitionReference
            {
                Id = taskId,
                VersionSpec = "2.*"
            }
        };

        var phase = new Phase
        {
            Name = "Build",
            Target = new AgentPoolQueueTarget()
        };
        phase.Steps.Add(step);

        var process = new DesignerProcess();
        process.Phases.Add(phase);

        var build = new BuildDefinition
        {
            Name = "My-Build",
            Process = process
        };

        var generator = CreateGenerator(build, null, taskMap);
        var result = generator.GenPipeline();

        Assert.Null(result);
    }

    [Fact]
    public void GenPipeline_BuildWithTrigger_YamlContainsTrigger()
    {
        var taskId = Guid.NewGuid();
        const int taskMajorVersion = 1;

        var taskObj = new TaskObj
        {
            id = taskId,
            name = "CmdLine",
            version = new CasCap.Models.TaskVersion { major = taskMajorVersion, minor = 0, patch = 0 },
            inputs = new List<TaskInput>(),
            inputMap = new Dictionary<string, TaskInput>()
        };

        var taskMap = new Dictionary<Guid, Dictionary<int, TaskObj>>
        {
            { taskId, new Dictionary<int, TaskObj> { { taskMajorVersion, taskObj } } }
        };

        var step = new BuildDefinitionStep
        {
            Enabled = true,
            DisplayName = "Run script",
            TaskDefinition = new Microsoft.TeamFoundation.Build.WebApi.TaskDefinitionReference
            {
                Id = taskId,
                VersionSpec = $"{taskMajorVersion}.*"
            }
        };

        var phase = new Phase
        {
            Name = "Build",
            Target = new AgentPoolQueueTarget()
        };
        phase.Steps.Add(step);

        var trigger = new ContinuousIntegrationTrigger();
        trigger.BranchFilters.Add("+main");

        var process = new DesignerProcess();
        process.Phases.Add(phase);

        var build = new BuildDefinition
        {
            Name = "My-Build",
            Process = process
        };
        build.Triggers.Add(trigger);

        var generator = new YamlPipelineGenerator(
            build,
            null,
            taskMap,
            new Dictionary<TaskGroupVersion, TaskGroup>(),
            new ConcurrentDictionary<TaskGroupVersion, Template>(),
            new Dictionary<int, Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup>(),
            false,
            DeployPhaseTypes.AgentBasedDeployment
        );

        var pipeline = generator.GenPipeline();

        Assert.NotNull(pipeline);
        Assert.NotNull(pipeline.trigger);
        var yaml = pipeline.ToString();
        Assert.Contains("trigger:", yaml);
        Assert.Contains("main", yaml);
    }

    [Fact]
    public void GenPipeline_BuildWithVariable_YamlContainsVariable()
    {
        var taskId = Guid.NewGuid();
        const int taskMajorVersion = 2;

        var taskObj = new TaskObj
        {
            id = taskId,
            name = "DotNetCoreCLI",
            version = new CasCap.Models.TaskVersion { major = taskMajorVersion, minor = 0, patch = 0 },
            inputs = new List<TaskInput>(),
            inputMap = new Dictionary<string, TaskInput>()
        };

        var taskMap = new Dictionary<Guid, Dictionary<int, TaskObj>>
        {
            { taskId, new Dictionary<int, TaskObj> { { taskMajorVersion, taskObj } } }
        };

        var step = new BuildDefinitionStep
        {
            Enabled = true,
            DisplayName = "Build",
            TaskDefinition = new Microsoft.TeamFoundation.Build.WebApi.TaskDefinitionReference
            {
                Id = taskId,
                VersionSpec = $"{taskMajorVersion}.*"
            }
        };

        var phase = new Phase
        {
            Name = "Build",
            Target = new AgentPoolQueueTarget()
        };
        phase.Steps.Add(step);

        var process = new DesignerProcess();
        process.Phases.Add(phase);

        var build = new BuildDefinition
        {
            Name = "My-Build",
            Process = process
        };
        build.Variables["buildConfiguration"] = new BuildDefinitionVariable { Value = "Release" };

        var generator = new YamlPipelineGenerator(
            build,
            null,
            taskMap,
            new Dictionary<TaskGroupVersion, TaskGroup>(),
            new ConcurrentDictionary<TaskGroupVersion, Template>(),
            new Dictionary<int, Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup>(),
            false,
            DeployPhaseTypes.AgentBasedDeployment
        );

        var pipeline = generator.GenPipeline();

        Assert.NotNull(pipeline);
        var yaml = pipeline.ToString();
        Assert.Contains("buildConfiguration", yaml);
        Assert.Contains("Release", yaml);
    }

    private static YamlPipelineGenerator CreateGenerator(
        BuildDefinition build,
        ReleaseDefinition release,
        Dictionary<Guid, Dictionary<int, TaskObj>> taskMap = null)
    {
        return new YamlPipelineGenerator(
            build,
            release,
            taskMap ?? new Dictionary<Guid, Dictionary<int, TaskObj>>(),
            new Dictionary<TaskGroupVersion, TaskGroup>(),
            new ConcurrentDictionary<TaskGroupVersion, Template>(),
            new Dictionary<int, Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup>(),
            false,
            DeployPhaseTypes.AgentBasedDeployment
        );
    }
}
