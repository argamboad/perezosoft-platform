using System.Collections.Concurrent;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Time.Testing;
using Perezosoft.Client;
using Perezosoft.Client.Auth;

namespace Perezosoft.Testing.Ui;

/// <summary>
/// The fake device clock, remembering the due time of every timer armed on it — so a test can wait for the code
/// under test to have SCHEDULED something (a retry, a re-arm) rather than guessing how long that takes on a loaded
/// runner (#381).
/// </summary>
public sealed class TestClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly ConcurrentQueue<TimeSpan> _armed = new();

    /// <summary>How many timers have been armed so far; pass it to <see cref="ArmedSince"/> later.</summary>
    public int ArmedCount => _armed.Count;

    /// <summary>The due times of the timers armed after the first <paramref name="mark"/> ones.</summary>
    public IReadOnlyList<TimeSpan> ArmedSince(int mark) => [.. _armed.Skip(mark)];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _armed.Enqueue(dueTime);
        return base.CreateTimer(callback, state, dueTime, period);
    }
}
