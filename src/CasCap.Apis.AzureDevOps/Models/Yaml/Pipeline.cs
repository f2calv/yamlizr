using AzurePipelinesToGitHubActionsConverter.Core.AzurePipelines;
using CasCap.Utilities;
using System.Collections.Generic;
using YamlDotNet.Serialization;
namespace CasCap.Models
{
    public class Pipeline : AzurePipelinesRoot<Trigger, List<Variable>>
    {
        public override string ToString()
        {
            var serializer = new SerializerBuilder()
                .WithEventEmitter(e => new LiteralMultilineEventEmitter(e))
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
                .DisableAliases()
                .Build();
            var str = serializer.Serialize(this);
            return str.Replace(": |-\r\n", ": |\r\n");
        }
    }
}