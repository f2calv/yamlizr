namespace CasCap.Models;

/// <summary>One declared input of an Azure Pipelines task.</summary>
/// <remarks>
/// Read from the task catalogue so a classic step's supplied values can be matched against what the
/// task actually declares. An input left at its declared default is omitted from the generated YAML.
/// </remarks>
public class TaskInput
{
    /// <summary>True when the task refuses to run without a value for this input.</summary>
    public bool required { get; set; }

    /// <summary>Permitted values, keyed by value, for an input rendered as a pick list.</summary>
    public Dictionary<string, string> options { get; set; }

    /// <summary>Alternative names accepted for this input, which a classic definition may have used.</summary>
    public List<string> aliases { get; set; }

    /// <summary>Value used when a step supplies none.</summary>
    public string defaultValue { get; set; }

    /// <summary>Name of the group this input is displayed under in the classic editor.</summary>
    public string groupName { get; set; }

    /// <summary>Help text shown beside the input, in Markdown.</summary>
    public string helpMarkDown { get; set; }

    /// <summary>Label shown beside the input in the classic editor.</summary>
    public string label { get; set; }

    /// <summary>Name the step uses to supply a value, and the key emitted under <c>inputs</c>.</summary>
    public string name { get; set; }

    /// <summary>Input type, for example <c>string</c>, <c>boolean</c>, <c>picklist</c> or <c>filePath</c>.</summary>
    public string type { get; set; }

    /// <summary>Expression controlling whether the classic editor shows this input.</summary>
    public string visibleRule { get; set; }

    /// <inheritdoc/>
    public override string ToString() => $"{name}";
}
