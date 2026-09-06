namespace CasCap.Models;

/// <summary>
/// One entry in a template's <c>parameters</c> declaration.
/// </summary>
/// <remarks>
/// A template that declares its parameters as a mapping is rejected by the Azure Pipelines schema,
/// which expects a sequence of these. Callers still pass values as a mapping, which is why
/// <see cref="Step.parameters"/> keeps that shape.
/// </remarks>
public class TemplateParameter
{
    /// <summary>Name callers supply a value under, and the name the template body references.</summary>
    public string name { get; set; }

    /// <summary>Classic task group inputs carry no usable type, so every parameter is a string.</summary>
    public string type { get; set; } = "string";

    /// <summary>Value used when a caller supplies none.</summary>
    /// <remarks>Taken from the task group input's default, and null when it declared none.</remarks>
    public string @default { get; set; }
}
