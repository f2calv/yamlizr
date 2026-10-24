using CasCap.Utilities;

namespace CasCap.Models;

/// <summary>Root of a generated Azure Pipelines YAML document.</summary>
/// <remarks>
/// Property order is the serialised order, so members are declared in the order the Azure Pipelines
/// schema conventionally presents them rather than alphabetically. Only one of <see cref="Stages"/>,
/// <see cref="Jobs"/> and <see cref="Steps"/> is populated, because a document that mixes them is
/// rejected.
/// <para>See <see href="https://learn.microsoft.com/azure/devops/pipelines/yaml-schema" />.</para>
/// </remarks>
public class Pipeline
{
    /// <summary>Build number format, emitted as the pipeline <c>name</c>.</summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>Parameters this document declares when it is used as a template.</summary>
    /// <remarks>A sequence, not a mapping; see <see cref="TemplateParameter"/>.</remarks>
    [YamlMember(Alias = "parameters")]
    public List<TemplateParameter>? Parameters { get; set; }

    /// <summary>Container image every job runs in.</summary>
    [YamlMember(Alias = "container")]
    public string? Container { get; set; }

    /// <summary>Repositories, containers and pipelines the run consumes.</summary>
    [YamlMember(Alias = "resources")]
    public Resources? Resources { get; set; }

    /// <summary>Continuous integration trigger.</summary>
    [YamlMember(Alias = "trigger")]
    public TriggerAzDO? Trigger { get; set; }

    /// <summary>Pull request trigger.</summary>
    [YamlMember(Alias = "pr")]
    public TriggerAzDO? Pr { get; set; }

    /// <summary>Scheduled triggers.</summary>
    [YamlMember(Alias = "schedules")]
    public Schedule[]? Schedules { get; set; }

    /// <summary>Agent pool every job runs on unless it overrides this.</summary>
    [YamlMember(Alias = "pool")]
    public Pool? Pool { get; set; }

    /// <summary>Matrix or parallel execution strategy.</summary>
    [YamlMember(Alias = "strategy")]
    public Strategy? Strategy { get; set; }

    /// <summary>Pipeline-scoped variables, including linked variable groups.</summary>
    /// <remarks>Omitted entirely when empty, because <c>variables: []</c> is rejected by the schema.</remarks>
    [YamlMember(Alias = "variables")]
    public List<Variable>? Variables { get; set; }

    /// <summary>Stages, used when the definition produced more than one.</summary>
    [YamlMember(Alias = "stages")]
    public StageAzDO[]? Stages { get; set; }

    /// <summary>Jobs, used when the definition produced a single stage with more than one job.</summary>
    [YamlMember(Alias = "jobs")]
    public Job[]? Jobs { get; set; }

    /// <summary>Steps, used when the definition produced a single job.</summary>
    [YamlMember(Alias = "steps")]
    public Step[]? Steps { get; set; }

    /// <summary>Service containers available to the run.</summary>
    [YamlMember(Alias = "services")]
    public Dictionary<string, string>? Services { get; set; }

    /// <summary>Serialises this pipeline to Azure Pipelines YAML.</summary>
    /// <remarks>
    /// Unset properties are omitted, aliases are disabled so a repeated object is written out in full
    /// rather than referenced, and <see cref="LiteralMultilineEventEmitter"/> keeps a multi-line
    /// script readable as a literal block scalar. The final replacement restores the trailing newline
    /// that the clip indicator would otherwise strip from such a block.
    /// </remarks>
    /// <returns>The YAML document.</returns>
    public override string ToString()
    {
        var serializer = new SerializerBuilder()
            .WithEventEmitter(e => new LiteralMultilineEventEmitter(e))
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .DisableAliases()
            .Build();
        var str = serializer.Serialize(this);
        return str.Replace(": |-\r\n", ": |\r\n");
    }
}
