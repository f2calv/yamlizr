namespace CasCap.Models;

/// <summary>One stage of a generated Azure Pipelines YAML document.</summary>
/// <remarks>
/// Deliberately does not inherit from <c>Stage</c>. Inheriting reorders the properties when
/// serialised, and the serialised order is the order they appear in the emitted YAML.
/// </remarks>
public class StageAzDO
{
    /// <summary>Stage identifier, which must match <c>[A-Za-z_][A-Za-z0-9_]*</c>.</summary>
    public string stage { get; set; }

    /// <summary>Human readable stage name.</summary>
    public string displayName { get; set; }

    /// <summary>Identifiers of the stages that must complete before this one starts.</summary>
    public string[] dependsOn { get; set; }

    /// <summary>Expression deciding whether the stage runs.</summary>
    public string condition { get; set; }

    /// <summary>Stage-scoped variables, including linked variable groups.</summary>
    /// <remarks>
    /// A <see cref="List{T}"/> of <see cref="Variable"/> rather than a name and value dictionary,
    /// because a stage may also reference a variable group or a variable template, which a dictionary
    /// cannot express. Omitted entirely when empty.
    /// </remarks>
    public List<Variable> variables { get; set; }

    /// <summary>Agent pool the stage's jobs run on.</summary>
    public Pool pool { get; set; }

    /// <summary>Jobs belonging to this stage.</summary>
    public Job[] jobs { get; set; }
}
