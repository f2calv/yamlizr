using System.ComponentModel.DataAnnotations;

namespace CasCap.Models;

public record AzureDevOpsOptions
{
    public const string ConfigurationSectionName = $"{nameof(CasCap)}:{nameof(AzureDevOpsOptions)}";

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
    [Required]
    public required string PAT { get; init; }
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
}
