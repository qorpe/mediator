using System.Collections.Concurrent;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// A bounded pool of keyed <see cref="SemaphoreSlim"/> instances with automatic eviction
/// of unused entries. Prevents unbounded memory growth when cache keys have high cardinality.
/// </summary>
internal sealed class BoundedLockPool
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);
    private readonly int _maxSize;
    private readonly TimeSpan _evictionInterval;
    private long _lastEvictionTicks;

    internal int Count => _locks.Count;

    internal BoundedLockPool(int maxSize = 10_000, TimeSpan? evictionInterval = null)
    {
        _maxSize = maxSize;
        _evictionInterval = evictionInterval ?? TimeSpan.FromMinutes(5);
        _lastEvictionTicks = Environment.TickCount64;
    }

    internal SemaphoreSlim GetOrCreate(string key)
    {
        if (_locks.TryGetValue(key, out var existing))
        {
            existing.Touch();
            return existing.Semaphore;
        }

        EvictIfNeeded();

        var entry = _locks.GetOrAdd(key, static _ => new LockEntry());
        entry.Touch();
        return entry.Semaphore;
    }

    private void EvictIfNeeded()
    {
        var now = Environment.TickCount64;
        var lastEviction = Interlocked.Read(ref _lastEvictionTicks);

        if (_locks.Count < _maxSize && now - lastEviction < _evictionInterval.TotalMilliseconds)
        {
            return;
        }

        // Only one thread should evict at a time
        if (Interlocked.CompareExchange(ref _lastEvictionTicks, now, lastEviction) != lastEviction)
        {
            return;
        }

        var threshold = now - (long)_evictionInterval.TotalMilliseconds;

        foreach (var kvp in _locks)
        {
            if (kvp.Value.LastAccessedTicks < threshold && kvp.Value.Semaphore.CurrentCount > 0)
            {
                _locks.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Forces eviction of stale entries. Primarily used for testing.
    /// </summary>
    internal void ForceEviction()
    {
        var now = Environment.TickCount64;
        var threshold = now - (long)_evictionInterval.TotalMilliseconds;

        foreach (var kvp in _locks)
        {
            if (kvp.Value.LastAccessedTicks < threshold && kvp.Value.Semaphore.CurrentCount > 0)
            {
                _locks.TryRemove(kvp.Key, out _);
            }
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public long LastAccessedTicks;

        public LockEntry()
        {
            LastAccessedTicks = Environment.TickCount64;
        }

        public void Touch()
        {
            Interlocked.Exchange(ref LastAccessedTicks, Environment.TickCount64);
        }
    }
}
