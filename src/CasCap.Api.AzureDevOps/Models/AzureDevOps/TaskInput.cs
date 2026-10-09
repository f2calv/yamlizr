namespace CasCap.Models;

/// <summary>One declared input of an Azure Pipelines task.</summary>
/// <remarks>
/// Read from the task catalogue so a classic step's supplied values can be matched against what the
/// task actually declares. An input left at its declared default is omitted from the generated YAML.
/// </remarks>
public class TaskInput
{
    /// <summary>True when the task refuses to run without a value for this input.</summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    /// <summary>Permitted values, keyed by value, for an input rendered as a pick list.</summary>
    [JsonPropertyName("options")]
    public Dictionary<string, string>? Options { get; set; }

    /// <summary>Alternative names accepted for this input, which a classic definition may have used.</summary>
    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    /// <summary>Value used when a step supplies none.</summary>
    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }

    /// <summary>Name of the group this input is displayed under in the classic editor.</summary>
    [JsonPropertyName("groupName")]
    public string? GroupName { get; set; }

    /// <summary>Help text shown beside the input, in Markdown.</summary>
    [JsonPropertyName("helpMarkDown")]
    public string? HelpMarkDown { get; set; }

    /// <summary>Label shown beside the input in the classic editor.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Name the step uses to supply a value, and the key emitted under <c>inputs</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Input type, for example <c>string</c>, <c>boolean</c>, <c>picklist</c> or <c>filePath</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Expression controlling whether the classic editor shows this input.</summary>
    [JsonPropertyName("visibleRule")]
    public string? VisibleRule { get; set; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name}";
}
