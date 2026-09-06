using CasCap.Utilities;

namespace CasCap.Models;

/// <summary>Root of a generated Azure Pipelines YAML document.</summary>
/// <remarks>
/// Property order is the serialised order, so members are declared in the order the Azure Pipelines
/// schema conventionally presents them rather than alphabetically. Only one of <see cref="stages"/>,
/// <see cref="jobs"/> and <see cref="steps"/> is populated, because a document that mixes them is
/// rejected.
/// <para>See <see href="https://learn.microsoft.com/azure/devops/pipelines/yaml-schema" />.</para>
/// </remarks>
public class Pipeline
{
    /// <summary>Build number format, emitted as the pipeline <c>name</c>.</summary>
    public string name { get; set; }

    /// <summary>Parameters this document declares when it is used as a template.</summary>
    /// <remarks>A sequence, not a mapping; see <see cref="TemplateParameter"/>.</remarks>
    public List<TemplateParameter> parameters { get; set; }

    /// <summary>Container image every job runs in.</summary>
    public string container { get; set; }

    /// <summary>Repositories, containers and pipelines the run consumes.</summary>
    public Resources resources { get; set; }

    /// <summary>Continuous integration trigger.</summary>
    public TriggerAzDO trigger { get; set; }

    /// <summary>Pull request trigger.</summary>
    public TriggerAzDO pr { get; set; }

    /// <summary>Scheduled triggers.</summary>
    public Schedule[] schedules { get; set; }

    /// <summary>Agent pool every job runs on unless it overrides this.</summary>
    public Pool pool { get; set; }

    /// <summary>Matrix or parallel execution strategy.</summary>
    public Strategy strategy { get; set; }

    /// <summary>Pipeline-scoped variables, including linked variable groups.</summary>
    /// <remarks>Omitted entirely when empty, because <c>variables: []</c> is rejected by the schema.</remarks>
    public List<Variable> variables { get; set; }

    /// <summary>Stages, used when the definition produced more than one.</summary>
    public StageAzDO[] stages { get; set; }

    /// <summary>Jobs, used when the definition produced a single stage with more than one job.</summary>
    public Job[] jobs { get; set; }

    /// <summary>Steps, used when the definition produced a single job.</summary>
    public Step[] steps { get; set; }

    /// <summary>Service containers available to the run.</summary>
    public Dictionary<string, string> services { get; set; }

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
