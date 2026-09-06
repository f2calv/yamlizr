using AzurePipelinesToGitHubActionsConverter.Core.AzurePipelines;
using CasCap.Models;
using CasCap.Utilities;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
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
    public static BuildDefinitionStep Step(Guid taskId, string versionSpec = "1.*", string displayName = "sample step", IDictionary<string, string> inputs = null)
        => new()
        {
            Enabled = true,
            DisplayName = displayName,
            TaskDefinition = new TaskDefinitionReference { Id = taskId, VersionSpec = versionSpec, DefinitionType = "task" },
            Inputs = inputs ?? new Dictionary<string, string>(),
        };

    /// <summary>Builds a classic designer build definition with one agent phase per supplied name.</summary>
    /// <remarks>
    /// More than one phase is required for the generator to emit jobs; a single phase is flattened to
    /// a bare step list, which hides the job identifier. Each phase depends on the one before it.
    /// </remarks>
    /// <param name="queueName">Agent queue name, emitted as the pipeline pool.</param>
    /// <param name="step">The step every phase carries.</param>
    /// <param name="phaseNames">Phase names, in order.</param>
    public static BuildDefinition BuildDefinitionWithPhases(string queueName, BuildDefinitionStep step, params string[] phaseNames)
    {
        var process = new DesignerProcess();

        for (var i = 0; i < phaseNames.Length; i++)
        {
            var phase = new Phase
            {
                Name = phaseNames[i],
                RefName = $"Phase_{i + 1}",
                Condition = "succeeded()",
                Target = new AgentPoolQueueTarget(),
                Steps = [step],
            };

            if (i > 0) phase.Dependencies.Add(new Dependency { Scope = $"Phase_{i}", Event = "Completed" });
            process.Phases.Add(phase);
        }

        return new BuildDefinition
        {
            Id = 43,
            Name = "sample multi phase build",
            Queue = new AgentPoolQueue { Name = queueName },
            Process = process,
        };
    }

    /// <summary>Builds a task map containing one installed task, keyed the way the generator expects.</summary>
    /// <remarks>
    /// Naming a task that really exists matters when the generated YAML is submitted to Azure DevOps,
    /// which rejects a reference to a task it cannot resolve.
    /// </remarks>
    public static Dictionary<Guid, Dictionary<int, TaskObj>> TaskMap(string taskName = "SampleTask", int major = 1, params string[] inputNames)
    {
        var task = new TaskObj
        {
            id = KnownTaskId,
            name = taskName,
            version = new CasCap.Models.TaskVersion { major = major },
            inputs = [.. inputNames.Select(p => new TaskInput { name = p })],
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

    /// <summary>Builds a classic release definition with one agent-based deploy phase per named environment.</summary>
    /// <param name="environmentNames">Environment names, in rank order.</param>
    public static ReleaseDefinition ReleaseDefinition(params string[] environmentNames)
    {
        //the SDK leaves these collections null, unlike the build definition equivalents
        var definition = new ReleaseDefinition
        {
            Id = 7,
            Name = "sample release",
            Environments = [],
        };

        var rank = 1;
        foreach (var environmentName in environmentNames)
        {
            var environment = new ReleaseDefinitionEnvironment
            {
                Name = environmentName,
                Rank = rank,
                DeployPhases = [],
                Variables = new Dictionary<string, ConfigurationVariableValue>(),
                VariableGroups = [],
            };

            environment.DeployPhases.Add(new AgentBasedDeployPhase
            {
                Name = "Agent job",
                Rank = 1,
                DeploymentInput = new AgentDeploymentInput { Condition = "succeeded()" },
                WorkflowTasks =
                {
                    new WorkflowTask
                    {
                        Enabled = true,
                        Name = "sample step",
                        TaskId = KnownTaskId,
                        Version = "1.*",
                        DefinitionType = "task",
                    }
                }
            });

            definition.Environments.Add(environment);
            rank++;
        }

        return definition;
    }

    /// <summary>Creates a generator over a release definition with no task groups or variable groups.</summary>
    public static YamlPipelineGenerator Generator(ReleaseDefinition release, Dictionary<Guid, Dictionary<int, TaskObj>> taskMap = null)
        => new(
            null,
            release,
            taskMap ?? TaskMap(),
            new Dictionary<TaskGroupVersion, TaskGroup>(),
            new ConcurrentDictionary<TaskGroupVersion, Template>(),
            new Dictionary<int, VariableGroup>(),
            inlineTaskGroups: false,
            DeployPhaseTypes.AgentBasedDeployment);
}
