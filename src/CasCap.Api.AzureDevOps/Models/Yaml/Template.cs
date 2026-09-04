using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace CasCap.Models;

public class Template : Pipeline
{
    [YamlIgnore]
    public TaskGroup taskGroup { get; set; }
}
