namespace CasCap.Models;

public class TaskObj
{
    public bool contentsUploaded { get; set; }
    public bool deprecated { get; set; }
    public bool preview { get; set; }
    public bool serverOwned { get; set; }
    public bool showEnvironmentVariables { get; set; }
    public Dictionary<string, TaskInput> inputMap { get; set; }
    public Guid id { get; set; }
    public List<string> demands { get; set; }
    public List<string> runsOn { get; set; }
    public List<string> satisfies { get; set; }
    public List<string> visibility { get; set; }
    public List<TaskInput> inputs { get; set; }
    public string author { get; set; }
    public string category { get; set; }
    public string contributionIdentifier { get; set; }
    public string contributionVersion { get; set; }
    public string definitionType { get; set; }
    public string description { get; set; }
    public string friendlyName { get; set; }
    public string helpMarkDown { get; set; }
    public string helpUrl { get; set; }
    public string iconUrl { get; set; }
    public string instanceNameFormat { get; set; }
    public string minimumAgentVersion { get; set; }
    public string name { get; set; }
    public string releaseNotes { get; set; }
    public TaskVersion version { get; set; }
}
