namespace CasCap.Models;

/// <summary>One stage of a generated Azure Pipelines YAML document.</summary>
/// <remarks>
/// Deliberately does not inherit from <c>Stage</c>. Inheriting reorders the properties when
/// serialised, and the serialised order is the order they appear in the emitted YAML.
/// </remarks>
public class StageAzDO
{
    /// <summary>Stage identifier, which must match <c>[A-Za-z_][A-Za-z0-9_]*</c>.</summary>
    [YamlMember(Alias = "stage")]
    public string? Stage { get; set; }

    /// <summary>Human readable stage name.</summary>
    [YamlMember(Alias = "displayName")]
    public string? DisplayName { get; set; }

    /// <summary>Identifiers of the stages that must complete before this one starts.</summary>
    [YamlMember(Alias = "dependsOn")]
    public string[]? DependsOn { get; set; }

    /// <summary>Expression deciding whether the stage runs.</summary>
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>Stage-scoped variables, including linked variable groups.</summary>
    /// <remarks>
    /// A <see cref="List{T}"/> of <see cref="Variable"/> rather than a name and value dictionary,
    /// because a stage may also reference a variable group or a variable template, which a dictionary
    /// cannot express. Omitted entirely when empty.
    /// </remarks>
    [YamlMember(Alias = "variables")]
    public List<Variable>? Variables { get; set; }

    /// <summary>Agent pool the stage's jobs run on.</summary>
    [YamlMember(Alias = "pool")]
    public Pool? Pool { get; set; }

    /// <summary>Jobs belonging to this stage.</summary>
    [YamlMember(Alias = "jobs")]
    public Job[]? Jobs { get; set; }
}
