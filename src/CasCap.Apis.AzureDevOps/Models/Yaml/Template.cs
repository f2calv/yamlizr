using Microsoft.TeamFoundation.DistributedTask.WebApi;
using YamlDotNet.Serialization;
namespace CasCap.Models
{
    public class Template : Pipeline
    {
        [YamlIgnore]
        public TaskGroup taskGroup { get; set; }
    }
}