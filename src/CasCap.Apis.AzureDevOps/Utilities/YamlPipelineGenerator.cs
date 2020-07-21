using AzurePipelinesToGitHubActionsConverter.Core.AzurePipelines;
using CasCap.Common.Extensions;
using CasCap.Models;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using Semver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace CasCap.Utilities
{
    public class YamlPipelineGenerator
    {
        readonly BuildDefinition _build;
        readonly ReleaseDefinition _release;
        readonly Dictionary<Guid, Dictionary<int, TaskObj>> _taskMap;
        readonly Dictionary<TaskGroupVersion, TaskGroup> _taskGroupMap;
        ConcurrentDictionary<TaskGroupVersion, Template> _taskGroupTemplateMap;//this collection is appended-to as the app iterates over the definitions
        readonly Dictionary<int, Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup> _variableGroupMap;
        readonly bool _inlineTaskGroups;

        readonly string _templatesFolder = "AzureDevOpsTaskGroups";

        enum VariableType
        {
            Build,
            Release
        }

        public YamlPipelineGenerator(
            BuildDefinition build,
            ReleaseDefinition release,
            Dictionary<Guid, Dictionary<int, TaskObj>> taskMap,
            Dictionary<TaskGroupVersion, TaskGroup> taskGroupMap,
            ConcurrentDictionary<TaskGroupVersion, Template> taskGroupTemplateMap,
            Dictionary<int, Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup> variableGroupMap,
            bool inlineTaskGroups
            )
        {
            _build = build;
            _release = release;
            _taskMap = taskMap;
            _taskGroupMap = taskGroupMap;
            _taskGroupTemplateMap = taskGroupTemplateMap;
            _variableGroupMap = variableGroupMap;
            _inlineTaskGroups = inlineTaskGroups;
        }

        public Pipeline GenPipeline()
        {
            var pipeline = new Pipeline();
            var stages = new List<Stage>();
            var jobs = new List<Job>();
            var steps = new List<Step>();
            if (_build is object && _release is null)//create build pipeline
            {
                pipeline.name = _build.BuildNumberFormat;
                pipeline.trigger = GenTrigger();
                if (_build.Queue is object)
                    pipeline.pool = new Pool { name = _build.Queue.Name };
                pipeline.variables = GenVariables(VariableType.Build);
                var buildStage = GenBuildStage();
                if (buildStage is object)
                    if (buildStage.jobs.Length == 1)
                        steps.AddRange(buildStage.jobs[0].steps);
                    else
                        jobs.AddRange(buildStage.jobs);
            }
            else if (_build is null && _release is object)//create release pipeline
            {
                pipeline.variables = GenVariables(VariableType.Release);
                if (pipeline.variables.Count == 0) pipeline.variables = null;
                var releaseStages = GenReleaseStages();
                if (releaseStages is object)
                {
                    if (releaseStages.Length == 1)
                        if (releaseStages[0].jobs.Length == 1)
                            steps.AddRange(releaseStages[0].jobs[0].steps);
                        else
                            jobs.AddRange(releaseStages[0].jobs);
                    else
                        stages.AddRange(releaseStages);
                }
            }
            else
                throw new Exception($"{nameof(YamlPipelineGenerator)} expects only either a build OR a release!");
            if (stages.Count > 1) pipeline.stages = stages.ToArray();
            else if (jobs.Count > 1) pipeline.jobs = jobs.ToArray();
            else pipeline.steps = steps.ToArray();
            return pipeline.stages.IsNullOrEmpty() && pipeline.jobs.IsNullOrEmpty() && pipeline.steps.IsNullOrEmpty() ? null : pipeline;
        }

        Stage GenBuildStage()
        {
            var phases = ((DesignerProcess)_build.Process).Phases.Where(p => p.Target is object && p.Target.Type == 1).ToList();
            if (phases.IsNullOrEmpty()) return null;
            var jobs = new List<Job>(phases.Count);
            var jobName = string.Empty;
            foreach (var phase in phases)
            {
                var steps = new List<Step>(phase.Steps.Count);
                foreach (var step in phase.Steps)
                    if (step.Enabled) steps.AddRange(GenSteps(step));
                if (steps.IsNullOrEmpty()) continue;
                var job = new Job
                {
                    cancelTimeoutInMinutes = phase.JobCancelTimeoutInMinutes,
                    condition = GenCondition(phase.Condition),
                    dependsOn = string.IsNullOrWhiteSpace(jobName) ? null : jobName,
                    displayName = phase.Name,
                    job = phase.Name,
                    steps = steps.ToArray(),
                    timeoutInMinutes = phase.JobTimeoutInMinutes,
                };
                jobs.Add(job);
                jobName = job.job;
            }
            return jobs.IsNullOrEmpty() ? null : new Stage { displayName = _build.Name, stage = _build.Name.Sanitize().Replace(" ", "_"), variables = GenVariables(VariableType.Build), jobs = jobs.ToArray() };
        }

        Trigger GenTrigger()
        {
            if (_build.Triggers.IsNullOrEmpty()) return null;
            foreach (var t in _build.Triggers.Where(p => p.TriggerType == DefinitionTriggerType.ContinuousIntegration))
            {
                var trigger = new Trigger();
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

        List<Variable> GenVariables(VariableType type, ReleaseDefinitionEnvironment environment = null)
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
                if (environment is object)
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

        string GenCondition(string condition) => string.IsNullOrWhiteSpace(condition) || condition.Equals("succeeded()", StringComparison.OrdinalIgnoreCase) ? "succeeded()" : condition;

        Stage[] GenReleaseStages()
        {
            if (_release.Environments.IsNullOrEmpty()) return null;
            var stages = new List<Stage>();
            foreach (var environment in _release.Environments)
            {
                var jobs = GenJobs(environment);
                if (jobs.IsNullOrEmpty()) continue;
                var variables = GenVariables(VariableType.Release, environment);
                var stage = new Stage
                {
                    displayName = _release.Name,
                    jobs = jobs.ToArray(),
                    stage = environment.Name.Sanitize("_"),
                    variables = variables.IsNullOrEmpty() ? null : variables,
                };
                stages.Add(stage);
            }
            return stages.IsNullOrEmpty() ? null : stages.ToArray();

            List<Job> GenJobs(ReleaseDefinitionEnvironment environment)
            {
                var jobName = string.Empty;
                var jobs = new List<Job>();
                foreach (var phase in environment.DeployPhases.Where(p => p.PhaseType == DeployPhaseTypes.AgentBasedDeployment).OrderBy(p => p.Rank))
                {
                    var steps = new List<Step>(phase.WorkflowTasks.Count);
                    foreach (var task in phase.WorkflowTasks)
                        if (task.Enabled)
                            steps.AddRange(GenSteps(task));
                    if (steps.IsNullOrEmpty()) continue;
                    var deploymentInput = phase.GetDeploymentInput();
                    var job = new Job
                    {
                        cancelTimeoutInMinutes = deploymentInput.JobCancelTimeoutInMinutes,
                        condition = GenCondition(deploymentInput.Condition),
                        dependsOn = string.IsNullOrWhiteSpace(jobName) ? null : jobName,
                        displayName = phase.Name,
                        job = phase.Name.Sanitize().Replace(" ", "_"),
                        steps = new List<Step>(steps).ToArray(),
                        timeoutInMinutes = deploymentInput.TimeoutInMinutes,
                    };
                    jobs.Add(job);
                    jobName = job.job;
                }
                return jobs;
            }
        }

        List<Step> GenSteps(BuildDefinitionStep task)
            => GenSteps(task.TaskDefinition.Id, task.DisplayName, task.TaskDefinition.VersionSpec, task.Inputs, task.Environment, task.Condition, task.ContinueOnError, task.TimeoutInMinutes);

        List<Step> GenSteps(WorkflowTask task)
            => GenSteps(task.TaskId, task.Name, task.Version, task.Inputs, task.Environment, task.Condition, task.ContinueOnError, task.TimeoutInMinutes);

        List<Step> GenSteps(TaskGroupStep task, Dictionary<string, string> parameters)
            => GenSteps(task.Task.Id, task.DisplayName, task.Task.VersionSpec, task.Inputs, task.Environment, task.Condition, task.ContinueOnError, task.TimeoutInMinutes, parameters);

        List<Step> GenSteps(Guid Id, string displayName, string semver, IDictionary<string, string> inputs, IDictionary<string, string> env,
            string condition, bool continueOnError, int timeoutInMinutes, Dictionary<string, string> parameters = null)
        {
            var version = SemVersion.Parse(semver.Replace(".*", ".0")).Major;
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
            return _inlineTaskGroups ? new List<Step>(template.steps) : GetSteps(template, inputs);

            Template GetOrCreateTaskGroupTemplate()
            {
                var key = new TaskGroupVersion(Id, version);
                if (_taskGroupTemplateMap.TryGetValue(key, out var template))
                    return template;
                else
                {
                    _taskGroupMap.TryGetValue(key, out var taskGroup);
                    template = new Template { taskGroup = taskGroup };
                    if (!taskGroup.Inputs.IsNullOrEmpty())
                    {
                        template.parameters = new Dictionary<string, string>(taskGroup.Inputs.Count);
                        foreach (var input in taskGroup.Inputs)
                            template.parameters.Add(input.Name, string.IsNullOrWhiteSpace(input.DefaultValue) ? null : input.DefaultValue);
                    }
                    var taskGroupSteps = taskGroup.Tasks.Where(p => p.Enabled).ToList();
                    if (!taskGroupSteps.IsNullOrEmpty())
                    {
                        var steps = new List<Step>(taskGroupSteps.Count());
                        foreach (var taskGroupStep in taskGroupSteps)
                            steps.AddRange(GenSteps(taskGroupStep, template.parameters));
                        template.steps = steps.ToArray();
                    }
                    template.steps = template.steps ?? new Step[0];//handle when all tasks within taskgroup are disabled
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
                    if (!_inlineTaskGroups && parameters is object)
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
                        if (i != lines.Length - 1) sb.Append("\n");
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
                if (input is object && string.IsNullOrWhiteSpace(inputs[key]))
                    inputs[key] = input.DefaultValue;
            }
            return new List<Step> { new Step { template = $"../{_templatesFolder}/{filename}", parameters = inputs.IsNullOrEmpty() ? null : inputs } };
        }
    }
}