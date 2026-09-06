using CasCap.Models;
using CasCap.Utilities;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using Microsoft.VisualStudio.Services.WebApi;
using System.Collections.Concurrent;
using TaskGroup = Microsoft.TeamFoundation.DistributedTask.WebApi.TaskGroup;
using VariableGroup = Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup;

namespace CasCap.Api.AzureDevOps.Tests.Integration;

/// <summary>
/// Converts every fixture definition in the live organisation and asks Azure DevOps to parse the
/// result, which is the end to end check issue #366 asks for.
/// </summary>
/// <remarks>
/// The hand-written samples in <see cref="PipelineValidationTests"/> prove the endpoint is wired up.
/// This proves the generator's real output is acceptable, so a regression in the generator fails the
/// build rather than shipping YAML that Azure DevOps refuses.
/// <para>
/// Task groups are inlined, because a relative <c>template:</c> reference resolves against the
/// target pipeline's repository rather than the submitted document.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public class FixtureConversionTests : TestBase
{
    /// <summary>Initialises the shared Azure DevOps connection.</summary>
    /// <param name="output">xUnit sink that test logging is written to.</param>
    public FixtureConversionTests(ITestOutputHelper output) : base(output) { }

    /// <summary>Name prefix every object created by <c>.scripts/New-FixtureDefinitions.ps1</c> carries.</summary>
    private const string FixturePrefix = "yamlizr.test.";

    private const string NotConfiguredForConversion =
        "No fixture organisation configured, set CasCap:AzureDevOpsOptions:PAT, OrganisationUri, Project and ValidationPipelineId.";

    private bool CanConvert => IsConfigured
        && Options.ValidationPipelineId.HasValue
        && !string.IsNullOrWhiteSpace(Options.OrganisationUri)
        && !string.IsNullOrWhiteSpace(Options.Project);

    [Fact]
    public async Task EveryFixtureDefinition_ConvertsToYamlThatAzureDevOpsAccepts()
    {
        Assert.SkipUnless(CanConvert, NotConfiguredForConversion);

        var cancellationToken = TestContext.Current.CancellationToken;
        var organisationUri = Options.OrganisationUri.TrimEnd('/');

        using var connection = new VssConnection(new Uri(organisationUri), new VssBasicCredential(string.Empty, Options.PAT));
        using var buildClient = await connection.GetClientAsync<BuildHttpClient>(cancellationToken);
        using var releaseClient = await connection.GetClientAsync<ReleaseHttpClient>(cancellationToken);
        using var taskAgentClient = await connection.GetClientAsync<TaskAgentHttpClient>(cancellationToken);

        var taskMap = await GetTaskMap(organisationUri);
        var taskGroupMap = await GetTaskGroupMap(taskAgentClient, cancellationToken);
        var variableGroupMap = (await taskAgentClient.GetVariableGroupsAsync(Options.Project, cancellationToken: cancellationToken))
            .ToDictionary(k => k.Id, v => v);

        var converted = 0;
        var failures = new List<string>();

        foreach (var reference in await buildClient.GetDefinitionsAsync(Options.Project, cancellationToken: cancellationToken))
        {
            if (!reference.Name.StartsWith(FixturePrefix, StringComparison.OrdinalIgnoreCase)) continue;

            var definition = await buildClient.GetDefinitionAsync(Options.Project, reference.Id, cancellationToken: cancellationToken);

            //the validation pipeline itself is YAML, and only a classic definition converts
            if (definition.Process is not DesignerProcess) continue;

            var yaml = Convert(definition, null, taskMap, taskGroupMap, variableGroupMap);
            if (yaml is null) continue;

            converted++;
            await Validate(definition.Name, yaml, failures, cancellationToken);
        }

        foreach (var reference in await releaseClient.GetReleaseDefinitionsAsync(Options.Project, cancellationToken: cancellationToken))
        {
            if (!reference.Name.StartsWith(FixturePrefix, StringComparison.OrdinalIgnoreCase)) continue;

            var definition = await releaseClient.GetReleaseDefinitionAsync(Options.Project, reference.Id, cancellationToken: cancellationToken);

            var yaml = Convert(null, definition, taskMap, taskGroupMap, variableGroupMap);
            if (yaml is null) continue;

            converted++;
            await Validate(definition.Name, yaml, failures, cancellationToken);
        }

        Assert.True(converted > 0, $"No definitions named '{FixturePrefix}*' were found, so nothing was validated.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    private static string Convert(
        BuildDefinition build,
        ReleaseDefinition release,
        Dictionary<Guid, Dictionary<int, TaskObj>> taskMap,
        Dictionary<TaskGroupVersion, TaskGroup> taskGroupMap,
        Dictionary<int, VariableGroup> variableGroupMap)
    {
        var generator = new YamlPipelineGenerator(
            build,
            release,
            taskMap,
            taskGroupMap,
            new ConcurrentDictionary<TaskGroupVersion, Template>(),
            variableGroupMap,
            inlineTaskGroups: true,
            DeployPhaseTypes.AgentBasedDeployment);

        return generator.GenPipeline()?.ToString();
    }

    private async Task Validate(string name, string yaml, List<string> failures, CancellationToken cancellationToken)
    {
        var result = await _apiSvc.Validate(
            Options.OrganisationUri, Options.Project, Options.ValidationPipelineId.Value, yaml, cancellationToken);

        if (!result.IsValid) failures.Add($"'{name}' was rejected: {result.Message}{Environment.NewLine}{yaml}");
    }

    private async Task<Dictionary<Guid, Dictionary<int, TaskObj>>> GetTaskMap(string organisationUri)
    {
        var extensions = await _apiSvc.GetAllExtensions(organisationUri);
        foreach (var extension in extensions)
            extension.inputMap = extension.inputs.ToDictionary(k => k.name, v => v);

        var taskMap = new Dictionary<Guid, Dictionary<int, TaskObj>>();
        foreach (var id in extensions.Select(p => p.id).Distinct())
        {
            //a duplicated id means an incorrectly installed extension, which the tool also tolerates
            var byMajorVersion = extensions.Where(p => p.id == id).ToDictionary(k => k.version.major, v => v);
            taskMap.TryAdd(id, byMajorVersion);
        }

        return taskMap;
    }

    private async Task<Dictionary<TaskGroupVersion, TaskGroup>> GetTaskGroupMap(
        TaskAgentHttpClient taskAgentClient, CancellationToken cancellationToken)
    {
        var taskGroups = await taskAgentClient.GetTaskGroupsAsync(Options.Project, cancellationToken: cancellationToken);

        var map = new Dictionary<TaskGroupVersion, TaskGroup>();
        foreach (var taskGroup in taskGroups)
            map[new TaskGroupVersion(taskGroup.Id, taskGroup.Version.Major)] = taskGroup;

        return map;
    }
}
