using System.Collections.Concurrent;
using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.Audit;

/// <summary>
/// In-memory implementation of <see cref="IAuditStore"/> for development and testing.
/// Thread-safe. Not suitable for production use with high throughput.
/// </summary>
public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly ConcurrentBag<AuditEntry> _entries = new();
    private readonly int _maxEntries;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryAuditStore"/>.
    /// </summary>
    /// <param name="maxEntries">The maximum number of entries to store. Defaults to 10,000.</param>
    public InMemoryAuditStore(int maxEntries = 10_000)
    {
        _maxEntries = maxEntries;
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        if (_entries.Count < _maxEntries)
        {
            _entries.Add(entry);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask SaveBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        for (int i = 0; i < entries.Count; i++)
        {
            if (_entries.Count >= _maxEntries)
            {
                break;
            }

            _entries.Add(entries[i]);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _entries.ToArray();
        var results = new List<AuditEntry>();

        for (int i = 0; i < snapshot.Length; i++)
        {
            var entry = snapshot[i];
            if (MatchesQuery(entry, query))
            {
                results.Add(entry);
            }
        }

        // Sort by timestamp descending
        results.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

        var paged = results
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();

        return new ValueTask<IReadOnlyList<AuditEntry>>(paged);
    }

    /// <summary>
    /// Gets all audit entries. For testing purposes.
    /// </summary>
    public IReadOnlyList<AuditEntry> GetAll() => _entries.ToArray();

    /// <summary>
    /// Clears all audit entries. For testing purposes.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    private static bool MatchesQuery(AuditEntry entry, AuditQuery query)
    {
        if (query.CorrelationId is not null && !string.Equals(entry.CorrelationId, query.CorrelationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.RequestType is not null && !string.Equals(entry.RequestType, query.RequestType, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.UserId is not null && !string.Equals(entry.UserId, query.UserId, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.From.HasValue && entry.Timestamp < query.From.Value)
        {
            return false;
        }

        if (query.To.HasValue && entry.Timestamp > query.To.Value)
        {
            return false;
        }

        if (query.IsSuccess.HasValue && entry.IsSuccess != query.IsSuccess.Value)
        {
            return false;
        }

        return true;
    }
}
