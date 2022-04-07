using AzurePipelinesToGitHubActionsConverter.Core;
using CasCap.Common.Extensions;
using CasCap.Models;
using CasCap.Utilities;
using Figgle;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using ShellProgressBar;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace CasCap.Commands;

[Command(Description = "Generate Azure DevOps YAML pipelines from classic definitions.")]
class GenerateCommand : CommandBase
{
    public GenerateCommand(ILogger<GenerateCommand> logger, ILoggerFactory loggerFactory, IConsole console)
        : base(logger, loggerFactory, console) { }

    [Required]
    [Option("-pat", Description = "Azure DevOps PAT (Personal Access Token).")]
    public string PAT { get; }

    [Required]
    [Option("-org|--organisation", Description = "Azure Devops Organisation name.")]
    public string organisation { get; }

    [Required]
    [Option("-proj|--project", Description = "Azure Devops Project name.")]
    public string project { get; }

    [Option("-out|--outputpath", Description = "Absolute path to YAML output folder [default: Current Directory]")]
    public string outputPath { get; set; }

    [Option("--filter", Description = "Build/Release definition wildcard filter.")]
    public string filter { get; }

    [Option("--parallelism", Description = "Parallel execution mode (work in progress) [default: false]")]
    public bool parallelism { get; }

    [Option("--inline", Description = "Inline taskgroup steps [default: false]")]
    public bool inlineTaskGroups { get; set; }

    [Option("--githubactions", Description = "Convert to GitHub Actions (also forces inline to true) [default: false]")]
    public bool gitHubActions { get; }

    public async Task<int> OnExecuteAsync()
    {
        if (gitHubActions) inlineTaskGroups = true;//github actions don't support templates

        if (string.IsNullOrWhiteSpace(PAT) || PAT.Trim().Length != 52)
        {
            _logger.LogError($"{nameof(PAT)} missing or invalid!");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(organisation))
        {
            _logger.LogError($"{nameof(organisation)} missing or invalid!");
            return 1;
        }

        #region tool intro text
        _console.WriteLine(FiggleFonts.Standard.Render(AppDomain.CurrentDomain.FriendlyName));

        var fgColor = _console.ForegroundColor;
        _console.WriteLine($"For documentaton/support;");

        _console.Write("- ");
        _console.ForegroundColor = ConsoleColor.Blue;
        _console.Write($"https://github.com/f2calv/yamlizr");
        _console.ForegroundColor = fgColor;
        _console.WriteLine($" (Azure Pipelines)");
        _console.Write("- ");
        _console.ForegroundColor = ConsoleColor.Blue;
        _console.Write($"https://github.com/samsmithnz/AzurePipelinesToGitHubActionsConverter");
        _console.ForegroundColor = fgColor;
        _console.WriteLine($" (GitHub Actions)");
        _console.ForegroundColor = fgColor;
        _console.WriteLine();
        _console.WriteLine($"To update/refresh this tool; ");
        _console.ForegroundColor = ConsoleColor.Cyan;
        _console.WriteLine($"   dotnet tool update --global yamlizr");
        _console.WriteLine();
        _console.ForegroundColor = fgColor;
        #endregion

        if (!Connect(PAT, organisation))
            return 1;

        if (!await GetProject(project))
            return 1;

        var rootPath = AppDomain.CurrentDomain.BaseDirectory;//or Directory.GetCurrentDirectory()?
        if (outputPath is object) rootPath = outputPath;
        //always output into a folder named after the project
        if (!Path.GetFileName(rootPath).Equals(_project.Name, StringComparison.OrdinalIgnoreCase))
            rootPath = Path.Combine(rootPath, _project.Name);
        if (!Directory.Exists(rootPath))
            if (Prompt.GetYesNo($"Directory '{rootPath}' does not exist, create?", true))
                Directory.CreateDirectory(rootPath);//create the output folder if doesn't exist
            else
                return 1;

        _console.WriteLine($"Pre-loading relevant Azure DevOps objects, this may take some time...");

        pbar = new ProgressBar(1, $"Loading build definition references...", pbarOptions);
        buildDefinitionReferences = await _buildClient.GetDefinitionsAsync(_project.Id);
        pbar.Tick($"{buildDefinitionReferences.Count} build definition reference(s) retrieved.");
        pbar.Dispose();
        buildDefinitions = new List<BuildDefinition>(buildDefinitionReferences.Count);

        pbar = new ProgressBar(1, $"Loading release definitions...", pbarOptions);
        releaseDefinitions = await _releaseClient.GetReleaseDefinitionsAsync(_project.Id);
        pbar.Tick($"{releaseDefinitions.Count} release definition(s) retrieved.");
        pbar.Dispose();

        pbar = new ProgressBar(1, $"Loading task groups...", pbarOptions);
        var taskGroups = await _taskAgentClient.GetTaskGroupsAsync(_project.Id);
        pbar.Tick($"{taskGroups.Count} task group(s) retrieved.");
        pbar.Dispose();
        var taskGroupMap = taskGroups.ToDictionary(k => new TaskGroupVersion(k.Id, k.Version.Major), v => taskGroups.FirstOrDefault(p => p.Id == v.Id && p.Version.Major == v.Version.Major));
        var taskGroupTemplateMap = new ConcurrentDictionary<TaskGroupVersion, Template>();

        pbar = new ProgressBar(1, $"Loading extensions...", pbarOptions);
        var tasks = await _apiSvc.GetAllExtensions(organisation);
        foreach (var task in tasks)
            task.inputMap = task.inputs.ToDictionary(k => k.name, v => v);
        pbar.Tick($"{tasks.Count} installed extension(s) retrieved.");
        pbar.Dispose();
        var azureDevOpsTaskMap = new Dictionary<Guid, Dictionary<int, TaskObj>>();
        foreach (var id in tasks.Select(p => p.id).Distinct())
        {
            var dExtensions = tasks.Where(p => p.id == id).ToDictionary(k => k.version.major, v => v);
            var azureDevOpsTask = dExtensions.First().Value;
            if (!azureDevOpsTaskMap.TryAdd(azureDevOpsTask.id, dExtensions))
            {
                //note: tasks can sometimes have a duplicated id if incorrectly installed!
                _console.WriteLine($"'{azureDevOpsTask.name}' has a non-unique extension id '{azureDevOpsTask.id}' so cannot be added to dictionary map");
            }
        }

        pbar = new ProgressBar(1, $"Loading variable groups...", pbarOptions);
        var variableGroups = await _taskAgentClient.GetVariableGroupsAsync(_project.Id);
        pbar.Tick($"{variableGroups.Count} variable group(s) retrieved.");
        pbar.Dispose();
        var variableGroupMap = variableGroups.ToDictionary(k => k.Id, v => v);

        pbar.Dispose();

        //new-up collection to store build/release definitions and pipelines
        var results = new List<(BuildDefinition buildDefinition, ReleaseDefinition releaseDefinition, Pipeline pipeline)>();
        var errors = new List<string>();

        //1) load all Designer build definitions
        if (!buildDefinitionReferences.IsNullOrEmpty())
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                _console.Write($"{buildDefinitionReferences.Count} build definition reference(s).");
                buildDefinitionReferences = buildDefinitionReferences.Where(p => p.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) > -1).ToList();
                _console.Write($" Filter set to '{filter}', {buildDefinitionReferences.Count} build definition(s) match filter.");
                _console.WriteLine();
            }

            var dtStart = DateTime.UtcNow;
            pbar = new ProgressBar(buildDefinitionReferences.Count, $"Retrieving {buildDefinitionReferences.Count} full build definition reference(s)...", pbarOptions) { EstimatedDuration = TimeSpan.FromMilliseconds(buildDefinitionReferences.Count * 100) };

            var processedDefinitionCount = 0;
            if (parallelism || 1 == 1)//always parallel as this is pretty solid...
                await buildDefinitionReferences.ForEachAsyncSemaphore(definitionReference => ProcessDefinition(definitionReference), Environment.ProcessorCount);
            else
                foreach (var definitionReference in buildDefinitionReferences)
                    await ProcessDefinition(definitionReference);

            pbar.Dispose();

            async Task ProcessDefinition(BuildDefinitionReference definitionReference)
            {
                var build = await _buildClient.GetDefinitionAsync(_project.Id, definitionReference.Id);

                if (build is object && build.Process is object)
                {
                    if (build.Process is DesignerProcess)
                        buildDefinitions.Add(build);
                }
                else
                    errors.Add($"Retrieval of build definition id {definitionReference.Id} '{definitionReference.Name}' failed");

                Interlocked.Increment(ref processedDefinitionCount);
                pbar.Tick(processedDefinitionCount, $"Retrieved {processedDefinitionCount} of {buildDefinitionReferences.Count} full build definition(s).");
                pbar.EstimatedDuration = TimeSpan.FromMilliseconds((buildDefinitionReferences.Count - processedDefinitionCount)
                    * DateTime.UtcNow.Subtract(dtStart).TotalMilliseconds / processedDefinitionCount);
            }
        }

        //2) process all build definitions
        if (!buildDefinitions.IsNullOrEmpty())
        {
            var dtStart = DateTime.UtcNow;
            pbar = new ProgressBar(buildDefinitions.Count, $"Processing {buildDefinitions.Count} classic designer build definition(s)...", pbarOptions) { EstimatedDuration = TimeSpan.FromMilliseconds(buildDefinitions.Count * 100) };

            var processedDefinitionCount = 0;
            if (parallelism)
                Parallel.ForEach(buildDefinitions, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, (buildDefinition) =>
                {
                    ProcessDefinition(buildDefinition);
                });
            else
                foreach (var buildDefinition in buildDefinitions)
                    ProcessDefinition(buildDefinition);

            pbar.Dispose();
            _console.WriteLine();//for some reason the progressbar gets corrupted so temporarily add blank line...

            void ProcessDefinition(BuildDefinition buildDefinition)
            {
                var generator = new YamlPipelineGenerator(
                    buildDefinition,
                    null,
                    azureDevOpsTaskMap,
                    taskGroupMap,
                    taskGroupTemplateMap,
                    variableGroupMap,
                    inlineTaskGroups
                    );

                var pipeline = generator.GenPipeline();
                if (pipeline is null)
                    errors.Add($"Processing build definition id {buildDefinition.Id} '{buildDefinition.Name}' failed");
                else
                    results.Add((buildDefinition, null, pipeline));

                Interlocked.Increment(ref processedDefinitionCount);
                pbar.Tick(processedDefinitionCount, $"{nameof(YamlPipelineGenerator)} processed {processedDefinitionCount} of {buildDefinitions.Count} build definition(s).");
                pbar.EstimatedDuration = TimeSpan.FromMilliseconds((buildDefinitions.Count - processedDefinitionCount)
                    * DateTime.UtcNow.Subtract(dtStart).TotalMilliseconds / processedDefinitionCount);
            }
        }

        //3) load and process all release definitions
        if (!releaseDefinitions.IsNullOrEmpty())
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                _console.Write($"{releaseDefinitions.Count} release definition(s).");
                releaseDefinitions = releaseDefinitions.Where(p => p.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) > -1).ToList();
                _console.WriteLine($" Filter set to '{filter}', {releaseDefinitions.Count} release definition(s) match filter.");
            }

            var dtStart = DateTime.UtcNow;
            pbar = new ProgressBar(releaseDefinitions.Count, $"Processing {releaseDefinitions.Count} release definition(s)...", pbarOptions) { EstimatedDuration = TimeSpan.FromMilliseconds(releaseDefinitions.Count * 100) };

            var processedDefinitionCount = 0;
            if (parallelism)
                await releaseDefinitions.ForEachAsyncSemaphore(releaseDefinition => ProcessDefinition(releaseDefinition), Environment.ProcessorCount);
            else
                foreach (var releaseDefinition in releaseDefinitions)
                    await ProcessDefinition(releaseDefinition);

            pbar.Dispose();
            _console.WriteLine();//for some reason the progressbar gets corrupted so temporarily add blank line...

            async Task ProcessDefinition(ReleaseDefinition releaseDefinition)
            {
                var release = await _releaseClient.GetReleaseDefinitionAsync(_project.Id, releaseDefinition.Id);

                var generator = new YamlPipelineGenerator(
                    null,
                    release,
                    azureDevOpsTaskMap,
                    taskGroupMap,
                    taskGroupTemplateMap,
                    variableGroupMap,
                    inlineTaskGroups
                    );

                var pipeline = generator.GenPipeline();
                if (pipeline is null)
                    errors.Add($"Processing release definition id {releaseDefinition.Id} '{releaseDefinition.Name}' failed");
                else
                    results.Add((null, releaseDefinition, pipeline));

                Interlocked.Increment(ref processedDefinitionCount);
                pbar.Tick(processedDefinitionCount, $"{nameof(YamlPipelineGenerator)} processed {processedDefinitionCount} of {releaseDefinitions.Count} release definition(s).");
                pbar.EstimatedDuration = TimeSpan.FromMilliseconds((releaseDefinitions.Count - processedDefinitionCount)
                    * DateTime.UtcNow.Subtract(dtStart).TotalMilliseconds / processedDefinitionCount);
            }
        }

        //todo: 4) construct multi-stage pipelines by pre-pending build stage onto release environment stages, connecting the two definitions via the Azure DevOps artifact

        //new-up Sam Smith's library
        //https://github.com/samsmithnz/AzurePipelinesToGitHubActionsConverter
        var conversion = new Conversion(false);

        var fileCounter = 0;

        //5) persist build stage to disk
        if (!buildDefinitions.IsNullOrEmpty())
        {
            var azureDevOpsPath = Path.Combine(rootPath, "AzureDevOpsBuilds");
            if (!Directory.Exists(azureDevOpsPath)) Directory.CreateDirectory(azureDevOpsPath);

            var gitHubPath = Path.Combine(rootPath, "GitHubBuilds");
            if (!Directory.Exists(gitHubPath) && gitHubActions) Directory.CreateDirectory(gitHubPath);

            var definitions = results.Where(p => p.buildDefinition is object).ToList();

            var dtStart = DateTime.UtcNow;
            pbar = new ProgressBar(definitions.Count, $"Persisting {definitions.Count} build pipeline(s) to disk{(gitHubActions ? " with GitHub Actions conversion" : string.Empty)}...", pbarOptions) { EstimatedDuration = TimeSpan.FromMilliseconds(releaseDefinitions.Count * 100) };

            var processedDefinitionCount = 0;
            if (parallelism || 1 == 1)//always parallel as this is pretty solid...
                await definitions.ForEachAsyncSemaphore(result => ProcessDefinition(result), Environment.ProcessorCount);
            else
                foreach (var result in definitions)
                    await ProcessDefinition(result);

            pbar.Dispose();
            _console.WriteLine();//for some reason the progressbar gets corrupted so temporarily add blank line...

            async Task ProcessDefinition((BuildDefinition buildDefinition, ReleaseDefinition releaseDefinition, Pipeline pipeline) result)
            {
                var fileCount = await WriteYAML(result.pipeline, result.buildDefinition.Id, result.buildDefinition.Name, azureDevOpsPath, gitHubPath);
                Interlocked.Add(ref fileCounter, fileCount);
                Interlocked.Increment(ref processedDefinitionCount);
                pbar.Tick(processedDefinitionCount, $"{AppDomain.CurrentDomain.FriendlyName} persisted {processedDefinitionCount} of {definitions.Count} build pipeline(s) to disk{(gitHubActions ? " with GitHub Actions conversion" : string.Empty)}.");
                pbar.EstimatedDuration = TimeSpan.FromMilliseconds((definitions.Count - processedDefinitionCount)
                    * DateTime.UtcNow.Subtract(dtStart).TotalMilliseconds / processedDefinitionCount);
            }
        }

        //6) persist release definition YAML to disk
        if (!releaseDefinitions.IsNullOrEmpty())
        {
            var azureDevOpsPath = Path.Combine(rootPath, "AzureDevOpsReleases");
            if (!Directory.Exists(azureDevOpsPath)) Directory.CreateDirectory(azureDevOpsPath);

            var gitHubPath = Path.Combine(rootPath, "GitHubReleases");
            if (!Directory.Exists(gitHubPath) && gitHubActions) Directory.CreateDirectory(gitHubPath);

            var definitions = results.Where(p => p.releaseDefinition is object).ToList();

            var dtStart = DateTime.UtcNow;
            pbar = new ProgressBar(definitions.Count, $"Persisting {definitions.Count} release pipeline(s) to disk{(gitHubActions ? " with GitHub Actions conversion" : string.Empty)}...", pbarOptions) { EstimatedDuration = TimeSpan.FromMilliseconds(releaseDefinitions.Count * 100) };

            var processedDefinitionCount = 0;
            if (parallelism || 1 == 1)//always parallel as this is pretty solid...
                await definitions.ForEachAsyncSemaphore(result => ProcessDefinition(result), Environment.ProcessorCount);
            else
                foreach (var result in definitions)
                    await ProcessDefinition(result);

            pbar.Dispose();
            _console.WriteLine();//for some reason the progressbar gets corrupted so temporarily add blank line...

            async Task ProcessDefinition((BuildDefinition buildDefinition, ReleaseDefinition releaseDefinition, Pipeline pipeline) result)
            {
                var fileCount = await WriteYAML(result.pipeline, result.releaseDefinition.Id, result.releaseDefinition.Name, azureDevOpsPath, gitHubPath);
                Interlocked.Add(ref fileCounter, fileCount);
                Interlocked.Increment(ref processedDefinitionCount);
                pbar.Tick(processedDefinitionCount, $"{AppDomain.CurrentDomain.FriendlyName} persisted {processedDefinitionCount} of {definitions.Count} release pipeline(s) to disk{(gitHubActions ? " with GitHub Actions conversion" : string.Empty)}.");
                pbar.EstimatedDuration = TimeSpan.FromMilliseconds((definitions.Count - processedDefinitionCount)
                    * DateTime.UtcNow.Subtract(dtStart).TotalMilliseconds / processedDefinitionCount);
            }
        }

        async Task<int> WriteYAML(Pipeline pipeline, int Id, string Name, string azureDevOpsPath, string gitHubPath)
        {
            var count = 0;
            if (pipeline is null)
            {
                errors.Add($"definition id {Id} '{Name}' cannot be YAML'ised at this time...");
                return count;
            }
            var azureDevOpsYAML = pipeline.ToString();
            var filename = $"{Name.Sanitize()}-{Id}.yml";
            var azureDevOpsDefPath = Path.Combine(azureDevOpsPath, filename);
            await File.WriteAllTextAsync(azureDevOpsDefPath, azureDevOpsYAML);
            count++;
            if (gitHubActions)
                try
                {
                    var gitHubYAML = conversion.ConvertAzurePipelineToGitHubAction(azureDevOpsYAML);
                    if (gitHubYAML is object && !string.IsNullOrWhiteSpace(gitHubYAML.actionsYaml))
                    {
                        var gitHubDefPath = Path.Combine(gitHubPath, filename);
                        File.WriteAllText(gitHubDefPath, gitHubYAML.actionsYaml);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"definition id {Id} '{Name}' GitHub Action conversion failed");
                    Debug.WriteLine(ex.Message);
                    //throw ex;
                }
            return count;
        }

        //7) persist all the YAML templates to disk
        if (!gitHubActions && !taskGroupTemplateMap.IsNullOrEmpty())
        {
            var azureDevOpsDefPath = Path.Combine(rootPath, "AzureDevOpsTaskGroups");
            if (!Directory.Exists(azureDevOpsDefPath)) Directory.CreateDirectory(azureDevOpsDefPath);

            //skip progress bar for templates until I have more time to refactor to above duplication
            _console.Write($"Persisting task group YAML templates...");
            foreach (var kvp in taskGroupTemplateMap)
            {
                var template = kvp.Value;
                var filename = $"{template.taskGroup.Name.Sanitize()}-v{kvp.Key.version}.yml";
                var azureDevOpsTaskGroupPath = Path.Combine(azureDevOpsDefPath, filename);
                File.WriteAllText(azureDevOpsTaskGroupPath, template.ToString());
                Interlocked.Increment(ref fileCounter);
            }
            _console.WriteLine($" done.");
        }

        if (!errors.IsNullOrEmpty())
        {
            _console.ForegroundColor = ConsoleColor.Red;
            _console.WriteLine($"{errors.Count} error(s) encountered when running Azure Designer -> Azure Pipeline conversion;");
            foreach (var error in errors)
                _console.WriteLine($"- {error}");
        }
        _console.ForegroundColor = ConsoleColor.Cyan;
        _console.WriteLine($"Total of {fileCounter} YAML file(s) created.");
        _console.ForegroundColor = fgColor;//reset the fg colour
        _console.WriteLine($"Exiting...");

        return 0;
    }
}