namespace StatusLight.Core.Layout;

/// <summary>
/// Assigns signals to a fixed number of slots (LEDs), keeping each key in the same slot for as long as it is present.
/// When there are more signals than slots, the last slot shows the most urgent of the signals without a slot of their own
/// (together with the signal that owns the last slot).
/// </summary>
public sealed class SlotAllocator(int slotCount)
{
    private readonly string?[] _slotKeys = new string?[slotCount];

    public int SlotCount => _slotKeys.Length;

    /// <param name="signals">The current signals for this zone; keys must be unique.</param>
    /// <returns>One entry per slot; <c>null</c> for an empty slot.</returns>
    public StatusSignal?[] Allocate(IReadOnlyList<StatusSignal> signals)
    {
        var byKey = signals.ToDictionary(s => s.Key);

        for (var i = 0; i < _slotKeys.Length; i++)
        {
            if (_slotKeys[i] is { } key && !byKey.ContainsKey(key))
                _slotKeys[i] = null;
        }

        var waiting = new Queue<StatusSignal>(signals
            .Where(s => Array.IndexOf(_slotKeys, s.Key) < 0)
            .OrderBy(s => s.UpdatedAt)
            .ThenBy(s => s.Key, StringComparer.Ordinal));

        for (var i = 0; i < _slotKeys.Length; i++)
        {
            if (_slotKeys[i] is null && waiting.TryDequeue(out var next))
                _slotKeys[i] = next.Key;
        }

        var result = new StatusSignal?[_slotKeys.Length];
        for (var i = 0; i < _slotKeys.Length; i++)
            result[i] = _slotKeys[i] is { } key ? byKey[key] : null;

        // Anything still waiting means every slot is taken, so the last slot is non-null.
        if (waiting.Count > 0)
        {
            var last = result.Length - 1;
            result[last] = waiting
                .Append(result[last]!)
                .OrderByDescending(s => s.State)
                .ThenByDescending(s => s.UpdatedAt)
                .First();
        }

        return result;
    }
}
