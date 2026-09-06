using CasCap.Common.Extensions;
using CasCap.Models;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using Semver;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace CasCap.Utilities;

/// <summary>Converts one classic Build or Release definition into an Azure Pipelines YAML document.</summary>
/// <remarks>
/// An instance handles a single definition, and <see cref="Warnings"/> is only complete once
/// <see cref="GenPipeline"/> has returned.
/// <para>
/// The conversion is deliberately incomplete: several classic constructs have no YAML equivalent.
/// Anything that cannot be represented is recorded in <see cref="Warnings"/> rather than dropped
/// silently, so the caller can report it.
/// </para>
/// </remarks>
public class YamlPipelineGenerator
{
    private readonly BuildDefinition _build;
    private readonly ReleaseDefinition _release;
    private readonly Dictionary<Guid, Dictionary<int, TaskObj>> _taskMap;
    private readonly Dictionary<TaskGroupVersion, TaskGroup> _taskGroupMap;
    ConcurrentDictionary<TaskGroupVersion, Template> _taskGroupTemplateMap;//this collection is appended-to as the app iterates over the definitions
    private readonly Dictionary<int, Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup> _variableGroupMap;
    private readonly bool _inlineTaskGroups;
    private readonly DeployPhaseTypes _phaseType;

    private readonly string _templatesFolder = "AzureDevOpsTaskGroups";

    private readonly List<string> _warnings = [];

    /// <summary>
    /// Classic constructs encountered during generation that could not be represented in YAML.
    /// </summary>
    /// <remarks>The caller is expected to surface these in its run summary.</remarks>
    public IReadOnlyList<string> Warnings => _warnings;

    enum VariableType
    {
        Build,
        Release
    }

    /// <summary>Creates a generator for a single definition.</summary>
    /// <remarks>Exactly one of <paramref name="build"/> and <paramref name="release"/> must be supplied.</remarks>
    /// <param name="build">The classic Build definition to convert, or null when converting a release.</param>
    /// <param name="release">The classic Release definition to convert, or null when converting a build.</param>
    /// <param name="taskMap">Installed tasks, keyed by task identifier then major version.</param>
    /// <param name="taskGroupMap">Task groups, keyed by identifier and version.</param>
    /// <param name="taskGroupTemplateMap">
    /// Templates generated so far, shared across every definition in the run and appended to as task
    /// groups are encountered, which is why it is concurrent.
    /// </param>
    /// <param name="variableGroupMap">Variable groups, keyed by identifier.</param>
    /// <param name="inlineTaskGroups">True to expand task group steps in place instead of emitting a template reference.</param>
    /// <param name="phaseType">The single deploy phase type to convert; other phases are reported and skipped.</param>
    public YamlPipelineGenerator(
        BuildDefinition build,
        ReleaseDefinition release,
        Dictionary<Guid, Dictionary<int, TaskObj>> taskMap,
        Dictionary<TaskGroupVersion, TaskGroup> taskGroupMap,
        ConcurrentDictionary<TaskGroupVersion, Template> taskGroupTemplateMap,
        Dictionary<int, Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup> variableGroupMap,
        bool inlineTaskGroups,
        DeployPhaseTypes phaseType
        )
    {
        _build = build;
        _release = release;
        _taskMap = taskMap;
        _taskGroupMap = taskGroupMap;
        _taskGroupTemplateMap = taskGroupTemplateMap;
        _variableGroupMap = variableGroupMap;
        _inlineTaskGroups = inlineTaskGroups;
        _phaseType = phaseType;
    }

    /// <summary>Converts the definition supplied to the constructor.</summary>
    /// <remarks>
    /// The result is flattened to the simplest shape that fits, because the schema rejects a document
    /// mixing them: several stages become <see cref="Pipeline.stages"/>, a single stage with several
    /// jobs becomes <see cref="Pipeline.jobs"/>, and a single job becomes <see cref="Pipeline.steps"/>.
    /// Flattening discards settings that only a stage or a job can carry, which is reported.
    /// </remarks>
    /// <returns>The generated pipeline, or null when the definition produced nothing convertible.</returns>
    /// <exception cref="GenericException">Thrown when neither or both of a build and a release were supplied.</exception>
    public Pipeline GenPipeline()
    {
        var pipeline = new Pipeline();
        var stages = new List<StageAzDO>();
        var jobs = new List<Job>();
        var steps = new List<Step>();
        if (_build is not null && _release is null)//create build pipeline
        {
            pipeline.name = _build.BuildNumberFormat;
            pipeline.trigger = GenTrigger();
            if (_build.Queue is not null)
                pipeline.pool = new Pool { name = _build.Queue.Name };
            var buildVariables = GenVariables(VariableType.Build);
            pipeline.variables = buildVariables.IsNullOrEmpty() ? null : buildVariables;
            var buildStage = GenBuildStage();
            if (buildStage is not null)
                if (buildStage.jobs.Length == 1)
                {
                    //flattening the only job to a bare step list discards its job-level settings, but a
                    //default condition is not worth reporting
                    var job = buildStage.jobs[0];
                    if (job.condition is not null && job.condition != "succeeded()")
                        _warnings.Add($"job '{job.job}' is the only job so its steps were flattened, dropping its condition '{job.condition}', see https://github.com/f2calv/yamlizr/issues/211");
                    steps.AddRange(job.steps);
                }
                else
                    jobs.AddRange(buildStage.jobs);
        }
        else if (_build is null && _release is not null)//create release pipeline
        {
            var releaseVariables = GenVariables(VariableType.Release);
            pipeline.variables = releaseVariables.IsNullOrEmpty() ? null : releaseVariables;
            var releaseStages = GenReleaseStages();
            if (releaseStages is not null)
            {
                if (releaseStages.Length == 1)
                {
                    //flattening the only stage discards its stage-level variables, which for a release
                    //are the environment-scoped variables and variable groups
                    var stage = releaseStages[0];
                    if (!stage.variables.IsNullOrEmpty())
                        _warnings.Add($"stage '{stage.stage}' is the only stage so it was flattened, dropping {stage.variables.Count} stage-level variable(s), see https://github.com/f2calv/yamlizr/issues/211");
                    if (stage.jobs.Length == 1)
                        steps.AddRange(stage.jobs[0].steps);
                    else
                        jobs.AddRange(stage.jobs);
                }
                else
                    stages.AddRange(releaseStages);
            }
        }
        else
            throw new GenericException($"{nameof(YamlPipelineGenerator)} expects only either a build OR a release!");
        if (stages.Count > 1) pipeline.stages = stages.ToArray();
        else if (jobs.Count > 1) pipeline.jobs = jobs.ToArray();
        else pipeline.steps = steps.ToArray();
        return pipeline.stages.IsNullOrEmpty() && pipeline.jobs.IsNullOrEmpty() && pipeline.steps.IsNullOrEmpty() ? null : pipeline;
    }

    StageAzDO GenBuildStage()
    {
        var allPhases = ((DesignerProcess)_build.Process).Phases;
        var phases = allPhases.Where(p => p.Target is not null && p.Target.Type == 1).ToList();
        //TODO(#182): only agent phases (Target.Type 1) are converted; server and deployment-group
        //phases are skipped. https://github.com/f2calv/yamlizr/issues/182
        if (allPhases.Count > phases.Count)
            _warnings.Add($"{allPhases.Count - phases.Count} non-agent build phase(s) are not converted, see https://github.com/f2calv/yamlizr/issues/182");
        if (phases.IsNullOrEmpty()) return null;

        // Identifiers are assigned up front, because a phase may depend on one declared after it.
        var usedJobIds = new HashSet<string>(phases.Count, StringComparer.OrdinalIgnoreCase);
        var converted = new List<(Phase phase, string jobId, List<Step> steps)>(phases.Count);
        var j = 0;
        foreach (var phase in phases)
        {
            var steps = new List<Step>(phase.Steps.Count);
            foreach (var step in phase.Steps)
                if (step.Enabled) steps.AddRange(GenSteps(step));
            if (steps.IsNullOrEmpty()) continue;
            //a classic phase can have no name, which used to throw from Sanitize().Replace()
            var phaseName = string.IsNullOrWhiteSpace(phase.Name) ? $"Phase {j + 1}" : phase.Name;
            converted.Add((phase, ToUniqueIdentifier(phaseName, $"Phase_{j + 1}", usedJobIds), steps));
            j++;
        }
        if (converted.IsNullOrEmpty()) return null;

        var jobIdByRefName = new Dictionary<string, string>(converted.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (phase, jobId, _) in converted)
            if (!string.IsNullOrWhiteSpace(phase.RefName)) jobIdByRefName[phase.RefName] = jobId;

        var jobs = new List<Job>(converted.Count);
        foreach (var (phase, jobId, steps) in converted)
        {
            var job = new Job
            {
                cancelTimeoutInMinutes = phase.JobCancelTimeoutInMinutes,
                condition = GenCondition(phase.Condition),
                dependsOn = GenDependsOn(phase, jobIdByRefName),
                displayName = string.IsNullOrWhiteSpace(phase.Name) ? jobId : phase.Name,
                job = jobId,
                steps = steps.ToArray(),
                timeoutInMinutes = phase.JobTimeoutInMinutes,
            };
            jobs.Add(job);
        }

        var stageVariables = GenVariables(VariableType.Build);
        return new StageAzDO
        {
            displayName = _build.Name,
            stage = ToIdentifier(_build.Name, "Build"),
            variables = stageVariables.IsNullOrEmpty() ? null : stageVariables,
            jobs = jobs.ToArray(),
        };
    }

    /// <summary>
    /// Maps a classic phase's declared dependencies onto the identifiers of the generated jobs.
    /// </summary>
    /// <remarks>
    /// A classic phase names its dependencies explicitly by refName, and may name more than one. This
    /// previously emitted the preceding job in iteration order instead, which serialised phases that
    /// were meant to run in parallel and silently dropped every dependency of a fan-in but the last.
    /// </remarks>
    private string[] GenDependsOn(Phase phase, Dictionary<string, string> jobIdByRefName)
    {
        if (phase.Dependencies.IsNullOrEmpty()) return null;

        var dependsOn = new List<string>(phase.Dependencies.Count);
        foreach (var dependency in phase.Dependencies)
        {
            if (jobIdByRefName.TryGetValue(dependency.Scope ?? string.Empty, out var jobId))
            {
                if (!dependsOn.Contains(jobId)) dependsOn.Add(jobId);
            }
            else
                _warnings.Add($"phase '{phase.Name}' depends on '{dependency.Scope}', which was not converted, so the dependency is missing from the generated YAML");
        }

        return dependsOn.Count == 0 ? null : dependsOn.ToArray();
    }

    TriggerAzDO GenTrigger()
    {
        if (_build.Triggers.IsNullOrEmpty()) return null;
        //TODO(#182): only continuous integration triggers are converted; pull request, scheduled and
        //build-completion triggers are skipped. https://github.com/f2calv/yamlizr/issues/182
        var unconverted = _build.Triggers.Where(p => p.TriggerType != DefinitionTriggerType.ContinuousIntegration).ToList();
        if (!unconverted.IsNullOrEmpty())
            _warnings.Add($"{unconverted.Count} trigger(s) of type {string.Join(", ", unconverted.Select(p => p.TriggerType).Distinct())} are not converted, see https://github.com/f2calv/yamlizr/issues/182");
        foreach (var t in _build.Triggers.Where(p => p.TriggerType == DefinitionTriggerType.ContinuousIntegration))
        {
            var trigger = new TriggerAzDO();
            var trig = (ContinuousIntegrationTrigger)t;
            if (!trig.BranchFilters.IsNullOrEmpty())
            {
                trigger.branches = new IncludeExclude();
                var include = new List<string>(trig.BranchFilters.Count);
                var exclude = new List<string>(trig.BranchFilters.Count);
                foreach (var branch in trig.BranchFilters)
                {
                    var b = branch.Substring(1).Replace("refs/heads/", string.Empty);
                    if (branch.StartsWith("+")) include.Add(b); else exclude.Add(b);
                }
                if (!include.IsNullOrEmpty()) trigger.branches.include = include.ToArray();
                if (!exclude.IsNullOrEmpty()) trigger.branches.exclude = exclude.ToArray();
            }
            if (!trig.PathFilters.IsNullOrEmpty())
            {
                trigger.paths = new IncludeExclude();
                var include = new List<string>(trig.PathFilters.Count);
                var exclude = new List<string>(trig.PathFilters.Count);
                foreach (var path in trig.PathFilters)
                {
                    var _path = path;
                    if (_path.Length == 2 && path[1] == '/') continue;
                    if (_path.StartsWith("+/")) _path = "+" + _path.Substring(2);
                    if (_path.StartsWith("-/")) _path = "-" + _path.Substring(2);
                    if (_path.StartsWith("+"))
                        include.Add(_path.Substring(1));
                    else
                        exclude.Add(_path.Substring(1));
                }
                if (!include.IsNullOrEmpty()) trigger.paths.include = include.ToArray();
                if (!exclude.IsNullOrEmpty()) trigger.paths.exclude = exclude.ToArray();
            }
            trigger.batch = trig.BatchChanges;
            return trigger;
        }
        return null;
    }

    private List<Variable> GenVariables(VariableType type, ReleaseDefinitionEnvironment environment = null)
    {
        List<Variable> variables;
        if (type == VariableType.Build)
        {
            variables = new List<Variable>(_build.VariableGroups.Count + _build.Variables.Count);
            foreach (var vg in _build.VariableGroups)
                variables.Add(new Variable { group = vg.Name });
            foreach (var kvp in _build.Variables)
                variables.Add(new Variable { name = kvp.Key, value = kvp.Value.Value });
        }
        else
        {
            if (environment is not null)
            {
                variables = new List<Variable>(environment.VariableGroups.Count + environment.Variables.Count);
                foreach (var id in environment.VariableGroups)
                    if (_variableGroupMap.TryGetValue(id, out var vg))
                        variables.Add(new Variable { group = vg.Name });
                foreach (var variable in environment.Variables)
                    variables.Add(new Variable { name = variable.Key, value = variable.Value.Value });
            }
            else
            {
                variables = new List<Variable>();
                if (!_release.VariableGroups.IsNullOrEmpty())
                    foreach (var id in _release.VariableGroups)
                        if (_variableGroupMap.TryGetValue(id, out var vg))
                            variables.Add(new Variable { group = vg.Name });
                foreach (var variable in _release.Variables)
                    variables.Add(new Variable { name = variable.Key, value = variable.Value.Value });
            }
        }
        return variables;
    }

    private static string GenCondition(string condition) => string.IsNullOrWhiteSpace(condition) || condition.Equals("succeeded()", StringComparison.OrdinalIgnoreCase) ? "succeeded()" : condition;

    /// <summary>
    /// Converts a classic phase or environment name into a YAML job or stage identifier.
    /// </summary>
    /// <remarks>
    /// Azure DevOps accepts almost anything as a classic phase name, but a YAML identifier must match
    /// <c>[A-Za-z_][A-Za-z0-9_]*</c>. Sanitize() is not enough on its own: it only strips characters
    /// that are illegal in a file name, so a comma or a full stop survives into the identifier and the
    /// pipeline is rejected on upload.
    /// </remarks>
    internal static string ToIdentifier(string name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            var mapped = char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_';
            //collapse runs, and drop leading ones, so "Phase three, fan-in" does not become Phase_three__fan_in
            if (mapped == '_' && (builder.Length == 0 || builder[^1] == '_')) continue;
            builder.Append(mapped);
        }

        while (builder.Length > 0 && builder[^1] == '_') builder.Length--;

        if (builder.Length == 0) return fallback;

        // An identifier may not start with a digit.
        var identifier = builder.ToString();
        return char.IsAsciiDigit(identifier[0]) ? $"_{identifier}" : identifier;
    }

    /// <summary>
    /// Returns the identifier for <paramref name="name"/>, suffixed only as far as needed to be unique
    /// within <paramref name="used"/>, to which the result is added.
    /// </summary>
    /// <remarks>
    /// Azure Pipelines requires job identifiers to be unique within a stage, and resolves dependsOn
    /// case-insensitively, so uniqueness is tracked that way. Collisions are tested for rather than
    /// predicted from the phase names, because two distinct names can sanitise to one identifier.
    /// </remarks>
    private static string ToUniqueIdentifier(string name, string fallback, HashSet<string> used)
    {
        var identifier = ToIdentifier(name, fallback);
        if (used.Add(identifier)) return identifier;

        var suffix = 1;
        string candidate;
        do candidate = $"{identifier}_{suffix++}";
        while (!used.Add(candidate));
        return candidate;
    }

    StageAzDO[] GenReleaseStages()
    {
        if (_release.Environments.IsNullOrEmpty()) return null;

        //TODO(#182): release artifacts are not converted. Each artifact should become a resource or a
        //download step; until then a generated release pipeline has no inputs.
        //https://github.com/f2calv/yamlizr/issues/182
        if (!_release.Artifacts.IsNullOrEmpty())
            _warnings.Add($"{_release.Artifacts.Count} release artifact(s) are not converted, see https://github.com/f2calv/yamlizr/issues/182");

        var stages = new List<StageAzDO>();
        foreach (var environment in _release.Environments)
        {
            var jobs = GenJobs(environment);
            if (jobs.IsNullOrEmpty()) continue;

            //TODO(#374): approvals and gates have no in-document YAML equivalent. The stage needs to
            //emit a deployment job targeting an Environment, and the checks are configured on that
            //Environment outside the pipeline. https://github.com/f2calv/yamlizr/issues/374
            if (HasApprovals(environment))
                _warnings.Add($"stage '{environment.Name}' has deployment approvals or gates which are not converted, see https://github.com/f2calv/yamlizr/issues/374");

            //TODO(#182): environment.Conditions carries the classic stage trigger, including
            //artifact and environment dependencies, which should become stage dependsOn/condition.
            //https://github.com/f2calv/yamlizr/issues/182
            var variables = GenVariables(VariableType.Release, environment);
            var stageName = ToIdentifier(environment.Name, $"Stage_{stages.Count + 1}");
            var stage = new StageAzDO
            {
                // The release definition names the document, not each stage within it.
                displayName = string.IsNullOrWhiteSpace(environment.Name) ? stageName : environment.Name,
                jobs = jobs.ToArray(),
                stage = stageName,
                variables = variables.IsNullOrEmpty() ? null : variables,
            };
            stages.Add(stage);
        }
        if (stages.Count > 1)
            _warnings.Add($"{stages.Count} stages were generated without dependsOn, so they will run concurrently rather than in the classic environment order, see https://github.com/f2calv/yamlizr/issues/182");
        return stages.IsNullOrEmpty() ? null : stages.ToArray();

        static bool HasApprovals(ReleaseDefinitionEnvironment environment)
            //classic environments always carry an automated approval, only a real gate is worth reporting
            => environment.PreDeployApprovals?.Approvals?.Any(p => !p.IsAutomated) == true
                || environment.PostDeployApprovals?.Approvals?.Any(p => !p.IsAutomated) == true
                || environment.PreDeploymentGates?.Gates?.Count > 0
                || environment.PostDeploymentGates?.Gates?.Count > 0;

        List<Job> GenJobs(ReleaseDefinitionEnvironment environment)
        {
            var jobName = string.Empty;
            var jobs = new List<Job>();
            var usedJobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            //TODO(#182): --phasetype selects a single deploy phase type, the rest are skipped.
            //https://github.com/f2calv/yamlizr/issues/182
            var skipped = environment.DeployPhases.Count(p => p.PhaseType != _phaseType);
            if (skipped > 0)
                _warnings.Add($"stage '{environment.Name}' has {skipped} deploy phase(s) that are not {_phaseType} and are not converted, see https://github.com/f2calv/yamlizr/issues/182");
            var j = 0;
            foreach (var phase in environment.DeployPhases.Where(p => p.PhaseType == _phaseType).OrderBy(p => p.Rank))
            {
                var steps = new List<Step>(phase.WorkflowTasks.Count);
                foreach (var task in phase.WorkflowTasks)
                    if (task.Enabled)
                        steps.AddRange(GenSteps(task));
                if (steps.IsNullOrEmpty()) continue;
                var deploymentInput = phase.GetDeploymentInput();
                //a classic deploy phase can have no name, which used to throw from Sanitize().Replace()
                var phaseName = string.IsNullOrWhiteSpace(phase.Name) ? $"Phase {j + 1}" : phase.Name;
                var job = new Job
                {
                    cancelTimeoutInMinutes = deploymentInput.JobCancelTimeoutInMinutes,
                    condition = GenCondition(deploymentInput.Condition),
                    dependsOn = string.IsNullOrWhiteSpace(jobName) ? null : new[] { jobName },
                    displayName = phaseName,
                    job = ToUniqueIdentifier(phaseName, $"Phase_{j + 1}", usedJobIds),
                    steps = new List<Step>(steps).ToArray(),
                    timeoutInMinutes = deploymentInput.TimeoutInMinutes,
                };
                jobs.Add(job);
                jobName = job.job;
                j++;
            }
            return jobs;
        }
    }

    private List<Step> GenSteps(BuildDefinitionStep task)
        => GenSteps(task.TaskDefinition.Id, task.DisplayName, task.TaskDefinition.VersionSpec, task.Inputs, task.Environment, task.Condition, task.ContinueOnError, task.TimeoutInMinutes);

    private List<Step> GenSteps(WorkflowTask task)
        => GenSteps(task.TaskId, task.Name, task.Version, task.Inputs, task.Environment, task.Condition, task.ContinueOnError, task.TimeoutInMinutes);

    private List<Step> GenSteps(TaskGroupStep task, Dictionary<string, string> parameters)
        => GenSteps(task.Task.Id, task.DisplayName, task.Task.VersionSpec, task.Inputs, task.Environment, task.Condition, task.ContinueOnError, task.TimeoutInMinutes, parameters);

    private List<Step> GenSteps(Guid Id, string displayName, string semver, IDictionary<string, string> inputs, IDictionary<string, string> env,
        string condition, bool continueOnError, int timeoutInMinutes, Dictionary<string, string> parameters = null)
    {
        if (!TryParseMajorVersion(semver, out var version))
        {
            _warnings.Add($"step '{displayName}' (task {Id}) has an unusable version '{semver}' and was not converted");
            return [];
        }
        if (_taskMap.TryGetValue(Id, out var taskObjs) && taskObjs.TryGetValue(version, out var taskObj))
            return new List<Step>
            {
                new Step
                {
                    condition = GenCondition(condition) == "succeeded()" ? null : GenCondition(condition),//todo: add "succeeded()" as default in Sam's lib
                    continueOnError = continueOnError,
                    displayName = displayName,
                    env = env.IsNullOrEmpty() ? null : new Dictionary<string, string>(env),
                    inputs = ProcessTaskInputs(new Dictionary<string, string>(inputs)),
                    task = string.IsNullOrWhiteSpace(taskObj.contributionIdentifier) ? $"{taskObj.name}@{version}"
                        : $"{taskObj.contributionIdentifier}.{taskObj.name}@{version}",
                    timeoutInMinutes = timeoutInMinutes,
                }
            };
        var template = GetOrCreateTaskGroupTemplate();
        if (template is null)
        {
            //Neither an installed task nor a known task group, e.g. the extension has since been
            //uninstalled. Previously this fell through to a Template with a null taskGroup and threw
            //an NRE, see https://github.com/f2calv/yamlizr/issues/177
            _warnings.Add($"step '{displayName}' references task or task group {Id} v{version}, which is not installed in this organisation, and was not converted");
            return [];
        }
        return _inlineTaskGroups ? new List<Step>(template.steps) : GetSteps(template, inputs);

        Template GetOrCreateTaskGroupTemplate()
        {
            var key = new TaskGroupVersion(Id, version);
            if (_taskGroupTemplateMap.TryGetValue(key, out var template))
                return template;
            else
            {
                if (!_taskGroupMap.TryGetValue(key, out var taskGroup))
                    return null;
                template = new Template { taskGroup = taskGroup };
                // Declared as a sequence for the schema, but substitution below needs a lookup.
                Dictionary<string, string> parameterDefaults = null;
                if (!taskGroup.Inputs.IsNullOrEmpty())
                {
                    template.parameters = new List<TemplateParameter>(taskGroup.Inputs.Count);
                    parameterDefaults = new Dictionary<string, string>(taskGroup.Inputs.Count);
                    foreach (var input in taskGroup.Inputs)
                    {
                        var defaultValue = string.IsNullOrWhiteSpace(input.DefaultValue) ? null : input.DefaultValue;
                        template.parameters.Add(new TemplateParameter { name = input.Name, @default = defaultValue });
                        parameterDefaults[input.Name] = defaultValue;
                    }
                }
                var taskGroupSteps = taskGroup.Tasks.Where(p => p.Enabled).ToList();
                if (!taskGroupSteps.IsNullOrEmpty())
                {
                    var steps = new List<Step>(taskGroupSteps.Count);
                    foreach (var taskGroupStep in taskGroupSteps)
                        steps.AddRange(GenSteps(taskGroupStep, parameterDefaults));
                    template.steps = steps.ToArray();
                }
                template.steps ??= Array.Empty<Step>();//handle when all tasks within taskgroup are disabled
                _taskGroupTemplateMap.TryAdd(key, template);
                return template;
            }
        }

        Dictionary<string, string> ProcessTaskInputs(Dictionary<string, string> inputs)
        {
            if (inputs.IsNullOrEmpty()) return null;

            var newInputs = new Dictionary<string, string>();//create a new dictionary to preserve the key order from the incoming
            foreach (var key in inputs.Keys.ToList())
            {
                var inputValue = inputs[key];

                //check for existance of the input key in the actual task keys (99.9% of times this is fine, however the task version in the definition could go stale...)
                if (!taskObj.inputMap.TryGetValue(key, out var sourceInput))
                    continue;

                //strip inputs where the default value matches
                if (inputValue == sourceInput.defaultValue)
                    continue;

                //strip leading/trailing whitespace from multi-line strings
                inputValue = MultiLineTrim(inputValue);

                //replace task group variables with parameters only if taskgroup templates are required
                if (!_inlineTaskGroups && parameters is not null)
                    inputValue = ConvertVarsTo2Params(inputValue);

                //replace task inputs with the primary/top-most task alias (if one exists)
                newInputs.Add(!sourceInput.aliases.IsNullOrEmpty() ? sourceInput.aliases[0] : key, inputValue);
            }

            return newInputs.IsNullOrEmpty() ? null : newInputs;

            static string MultiLineTrim(string input)
            {
                var sb = new StringBuilder();
                if (string.IsNullOrWhiteSpace(input)) return sb.ToString();
                var lines = input.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var str = i == 0 || i == lines.Length - 1 ? lines[i].Trim() : lines[i].TrimEnd();
                    sb.Append(str);
                    if (i != lines.Length - 1) sb.Append('\n');
                }
                return sb.ToString();
            }

            string ConvertVarsTo2Params(string val)
            {
                if (string.IsNullOrWhiteSpace(val)) return val;
                foreach (var param in parameters)
                {
                    var replacement = $"${{{{ parameters.{param.Key} }}}}";
                    foreach (var pattern in new[] { $@"\$\({param.Key}\)", $@"variables\['{param.Key}'\]" })
                    {
                        var match = new Regex(pattern, RegexOptions.IgnoreCase);
                        val = match.Replace(val, replacement);
                    }
                }
                return val;
            }
        }
    }

    List<Step> GetSteps(Template template, IDictionary<string, string> inputs) => GenSteps(template, new Dictionary<string, string>(inputs));

    List<Step> GenSteps(Template template, Dictionary<string, string> inputs)
    {
        var filename = $"{template.taskGroup.Name.Sanitize()}-v{template.taskGroup.Version.Major}.yml";
        foreach (var key in inputs.Keys.ToList())
        {
            var input = template.taskGroup.Inputs.FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (input is not null && string.IsNullOrWhiteSpace(inputs[key]))
                inputs[key] = input.DefaultValue;
        }
        return new List<Step> { new Step { template = $"../{_templatesFolder}/{filename}", parameters = inputs.IsNullOrEmpty() ? null : inputs } };
    }

    /// <summary>Reads the major version from a classic task version spec such as <c>2.*</c>.</summary>
    static bool TryParseMajorVersion(string semver, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(semver)) return false;
        if (!SemVersion.TryParse(semver.Replace(".*", ".0"), SemVersionStyles.OptionalPatch, out var version)) return false;
        if (version.Major < int.MinValue || version.Major > int.MaxValue) return false;
        major = (int)version.Major;
        return true;
    }
}
