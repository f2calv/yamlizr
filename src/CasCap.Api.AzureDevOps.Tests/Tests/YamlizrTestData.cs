using AzurePipelinesToGitHubActionsConverter.Core.AzurePipelines;
using CasCap.Models;
using CasCap.Utilities;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using System.Collections.Concurrent;
using TaskDefinitionReference = Microsoft.TeamFoundation.Build.WebApi.TaskDefinitionReference;
using TaskGroup = Microsoft.TeamFoundation.DistributedTask.WebApi.TaskGroup;
using VariableGroup = Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup;

namespace CasCap.Api.AzureDevOps.Tests;

/// <summary>
/// Builders for the Azure DevOps objects the generator consumes, so generator behaviour can be
/// verified offline rather than against a live organisation.
/// </summary>
public static class YamlizrTestData
{
    /// <summary>Well-known identifier used by the installed task in these fixtures.</summary>
    public static Guid KnownTaskId { get; } = new("11111111-1111-1111-1111-111111111111");

    /// <summary>Identifier that is deliberately absent from both the task map and the task group map.</summary>
    public static Guid UnknownTaskId { get; } = new("99999999-9999-9999-9999-999999999999");

    /// <summary>Builds a classic designer build definition with a single agent phase.</summary>
    public static BuildDefinition BuildDefinition(params BuildDefinitionStep[] steps)
        => BuildDefinition("Phase 1", steps);

    /// <summary>Builds a classic designer build definition whose single agent phase carries the supplied name.</summary>
    /// <remarks>A null name reproduces the classic definitions reported in issue #177.</remarks>
    public static BuildDefinition BuildDefinition(string phaseName, params BuildDefinitionStep[] steps)
    {
        var definition = new BuildDefinition
        {
            Id = 42,
            Name = "sample build",
            BuildNumberFormat = "$(Date:yyyyMMdd)$(Rev:.r)",
            Process = new DesignerProcess
            {
                Phases =
                {
                    new Phase
                    {
                        Name = phaseName,
                        Condition = "succeeded()",
                        Target = new AgentPoolQueueTarget(),
                        Steps = [.. steps],
                    }
                }
            }
        };
        return definition;
    }

    /// <summary>Builds a single enabled step referencing the supplied task id and version spec.</summary>
    public static BuildDefinitionStep Step(Guid taskId, string versionSpec = "1.*", string displayName = "sample step")
        => new()
        {
            Enabled = true,
            DisplayName = displayName,
            TaskDefinition = new TaskDefinitionReference { Id = taskId, VersionSpec = versionSpec, DefinitionType = "task" },
        };

    /// <summary>Builds a task map containing one installed task, keyed the way the generator expects.</summary>
    public static Dictionary<Guid, Dictionary<int, TaskObj>> TaskMap(string taskName = "SampleTask", int major = 1)
    {
        var task = new TaskObj
        {
            id = KnownTaskId,
            name = taskName,
            version = new CasCap.Models.TaskVersion { major = major },
            inputs = [],
        };
        task.inputMap = task.inputs.ToDictionary(k => k.name, v => v);
        return new Dictionary<Guid, Dictionary<int, TaskObj>> { [KnownTaskId] = new() { [major] = task } };
    }

    /// <summary>Creates a generator over a build definition with no task groups or variable groups.</summary>
    public static YamlPipelineGenerator Generator(BuildDefinition build, Dictionary<Guid, Dictionary<int, TaskObj>> taskMap = null)
        => new(
            build,
            null,
            taskMap ?? TaskMap(),
            new Dictionary<TaskGroupVersion, TaskGroup>(),
            new ConcurrentDictionary<TaskGroupVersion, Template>(),
            new Dictionary<int, VariableGroup>(),
            inlineTaskGroups: false,
            DeployPhaseTypes.AgentBasedDeployment);
}
