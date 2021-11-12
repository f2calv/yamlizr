namespace CasCap.Models;

public class AzureDevOpsOptions
{
    public const string SectionKey = $"{nameof(CasCap)}:{nameof(AzureDevOpsOptions)}";

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
    public string PAT { get; set; }
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
}