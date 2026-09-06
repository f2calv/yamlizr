using YamlDotNet.Core;
using YamlDotNet.Serialization.EventEmitters;

namespace CasCap.Utilities;

/// <summary>Emits any multi-line string as a literal block scalar.</summary>
/// <remarks>
/// Without this, YamlDotNet writes a multi-line inline script as a quoted scalar with escaped
/// newlines, which is valid YAML but unreadable and impossible to edit by hand. Converted pipelines
/// are expected to be reviewed, so readability matters.
/// </remarks>
public class LiteralMultilineEventEmitter : ChainedEventEmitter
{
    /// <summary>Wraps the next emitter in the chain.</summary>
    /// <param name="nextEmitter">The emitter to delegate to once the style has been set.</param>
    public LiteralMultilineEventEmitter(IEventEmitter nextEmitter) : base(nextEmitter) { }

    /// <inheritdoc/>
    public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
    {
        if (eventInfo.Source.Type == typeof(string) && eventInfo.Source.Value is string value && value.Contains("\n"))
            eventInfo.Style = ScalarStyle.Literal;
        base.Emit(eventInfo, emitter);
    }
}
