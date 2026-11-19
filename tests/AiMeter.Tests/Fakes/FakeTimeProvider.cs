using System;
using System.Collections.Generic;

namespace AiMeter.Tests.Fakes;

/// <summary>
/// Hand-written <see cref="TimeProvider"/> whose timers fire only from <see cref="Advance"/>,
/// so tests can drive <c>Task.Delay(delay, timeProvider, ct)</c> without waiting.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<FakeTimer> _timers = new();
    private DateTimeOffset _utcNow;
    private long _timestamp;

    public FakeTimeProvider(DateTimeOffset? start = null)
    {
        _utcNow = start ?? new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _utcNow;
        }
    }

    public override long GetTimestamp()
    {
        lock (_lock)
        {
            return _timestamp;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_lock)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves the clock forward and fires every timer that became due.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by));

        List<FakeTimer> due;
        lock (_lock)
        {
            _utcNow += by;
            _timestamp += by.Ticks;

            due = new List<FakeTimer>();
            foreach (var timer in _timers)
            {
                if (!timer.Disposed && timer.IsDue(_timestamp))
                {
                    due.Add(timer);
                }
            }

            foreach (var timer in due)
            {
                timer.Reschedule();
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private void Remove(FakeTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly FakeTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private long _dueAt;

        public FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _dueAt = long.MaxValue;
        }

        public bool Disposed { get; private set; }

        public TimeSpan Period { get; private set; } = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._lock)
            {
                if (Disposed) return false;

                Period = period;
                _dueAt = dueTime == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : _owner._timestamp + dueTime.Ticks;
                return true;
            }
        }

        public bool IsDue(long timestamp)
        {
            lock (_owner._lock)
            {
                return _dueAt != long.MaxValue && timestamp >= _dueAt;
            }
        }

        public void Reschedule()
        {
            lock (_owner._lock)
            {
                _dueAt = Period == Timeout.InfiniteTimeSpan ? long.MaxValue : _dueAt + Period.Ticks;
            }
        }

        public void Fire() => _callback(_state);

        public void Dispose()
        {
            lock (_owner._lock)
            {
                if (Disposed) return;

                Disposed = true;
            }

            _owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
