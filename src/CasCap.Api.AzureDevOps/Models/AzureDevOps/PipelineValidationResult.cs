namespace CasCap.Models;

/// <summary>Outcome of submitting a YAML document to the Azure DevOps pipeline preview endpoint.</summary>
/// <remarks>
/// A preview parses the document and expands its templates without queueing anything, so it is the
/// closest thing to a compiler for a generated pipeline. It is stricter than a schema check in some
/// places and looser in others: an invalid job identifier is rejected, while <c>variables: []</c> is
/// accepted even though the editor schema flags it.
/// </remarks>
public record PipelineValidationResult
{
    /// <summary>True when Azure DevOps parsed the document successfully.</summary>
    public bool IsValid { get; init; }

    /// <summary>The document with every template expanded, populated only when <see cref="IsValid"/> is true.</summary>
    public string FinalYaml { get; init; }

    /// <summary>Why the document was rejected, populated only when <see cref="IsValid"/> is false.</summary>
    /// <remarks>Carries the file, line and column when Azure DevOps reports them.</remarks>
    public string Message { get; init; }
}
