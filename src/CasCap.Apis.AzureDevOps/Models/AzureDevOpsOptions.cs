﻿namespace CasCap.Models;

public class AzureDevOpsOptions
{
    public static string sectionKey = $"{nameof(CasCap)}:{nameof(AzureDevOpsOptions)}";

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
    public string PAT { get; set; }
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
}