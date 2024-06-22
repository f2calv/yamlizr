using AzurePipelinesToGitHubActionsConverter.Core.AzurePipelines;
namespace CasCap.Models;

public class TriggerAzDO : Trigger
{
    public new bool batch { get; set; }
    //public new bool autoCancel { get; set; }
}
