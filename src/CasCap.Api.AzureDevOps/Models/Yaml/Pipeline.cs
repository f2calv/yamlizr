using CasCap.Utilities;

namespace CasCap.Models;

public class Pipeline
{
    public string name { get; set; }
    public List<TemplateParameter> parameters { get; set; }
    public string container { get; set; }
    public Resources resources { get; set; }
    public TriggerAzDO trigger { get; set; }
    public TriggerAzDO pr { get; set; }
    public Schedule[] schedules { get; set; }
    public Pool pool { get; set; }
    public Strategy strategy { get; set; }
    public List<Variable> variables { get; set; }
    public StageAzDO[] stages { get; set; }
    public Job[] jobs { get; set; }
    public Step[] steps { get; set; }
    public Dictionary<string, string> services { get; set; }

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
