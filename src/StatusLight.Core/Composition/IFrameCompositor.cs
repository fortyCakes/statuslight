using StatusLight.Core.Effects;

namespace StatusLight.Core.Composition;

/// <summary>Decides what each LED shows, given the current signals.</summary>
public interface IFrameCompositor
{
    /// <returns>Exactly <paramref name="ledCount"/> effects, one per LED.</returns>
    LedEffect[] Compose(IReadOnlyList<StatusSignal> signals, int ledCount);
}
