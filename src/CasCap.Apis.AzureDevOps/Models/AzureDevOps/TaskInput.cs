namespace CasCap.Models;

public class TaskInput
{
    public bool required { get; set; }
    public Dictionary<string, string> options { get; set; }
    public List<string> aliases { get; set; }
    public string defaultValue { get; set; }
    public string groupName { get; set; }
    public string helpMarkDown { get; set; }
    public string label { get; set; }
    public string name { get; set; }
    public string type { get; set; }
    public string visibleRule { get; set; }

    public override string ToString() => $"{name}";
}
