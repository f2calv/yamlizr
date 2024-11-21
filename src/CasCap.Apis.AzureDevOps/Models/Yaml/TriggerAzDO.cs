using AzurePipelinesToGitHubActionsConverter.Core.AzurePipelines;

namespace CasCap.Models;

public class TriggerAzDO : Trigger
{
    // comment out batch else get the error Unhandled exception. System.Reflection.AmbiguousMatchException: Ambiguous match found for 'CasCap.Models.TriggerAzDO Boolean batch'.
    //public new bool batch { get; set; }
    //public new bool autoCancel { get; set; }
}
