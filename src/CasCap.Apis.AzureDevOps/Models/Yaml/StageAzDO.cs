using AzurePipelinesToGitHubActionsConverter.Core.AzurePipelines;
namespace CasCap.Models;

public class StageAzDO// : Stage //we can't inherit from Stage here as it will mess up the order of the properties when YAMLised.
{
    public string stage { get; set; }
    public string displayName { get; set; }
    public string[] dependsOn { get; set; }
    public string condition { get; set; }
    //public Dictionary<string, string> variables { get; set; }
    //override variables as we need the distinction between groups & templates
    public List<Variable> variables { get; set; }
    public Pool pool { get; set; }
    public Job[] jobs { get; set; }
}