namespace CasCap.Models;

/// <summary>A continuous integration or pull request trigger.</summary>
/// <remarks>
/// Exists to let the emitted property order be controlled independently of <see cref="Trigger"/>.
/// <para>
/// Hiding the inherited <c>batch</c> and <c>autoCancel</c> properties with <c>new</c> is not viable:
/// the serialiser then throws <see cref="System.Reflection.AmbiguousMatchException"/> because two
/// properties of the same name are visible on the type.
/// </para>
/// </remarks>
public class TriggerAzDO : Trigger
{
}
