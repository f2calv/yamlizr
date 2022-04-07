using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
namespace CasCap.Utilities;

public class LiteralMultilineEventEmitter : ChainedEventEmitter
{
    public LiteralMultilineEventEmitter(IEventEmitter nextEmitter) : base(nextEmitter) { }

    public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
    {
        if (eventInfo.Source.Type == typeof(string) && eventInfo.Source.Value is string value && value.Contains("\n"))
            eventInfo.Style = ScalarStyle.Literal;
        base.Emit(eventInfo, emitter);
    }
}