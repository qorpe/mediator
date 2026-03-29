using Microsoft.Extensions.Logging;
using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// Default scoped implementation of <see cref="IPostCommitTaskQueue"/>.
/// Collects fire-and-forget tasks during handler execution; TransactionBehavior
/// calls ExecuteAsync after the DB transaction has committed.
/// Tasks execute sequentially; failures are logged but do not throw.
/// </summary>
public sealed class PostCommitTaskQueue : IPostCommitTaskQueue
{
    private readonly List<Func<CancellationToken, Task>> _tasks = [];
    private readonly ILogger<PostCommitTaskQueue> _logger;

    public PostCommitTaskQueue(ILogger<PostCommitTaskQueue> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Enqueue(Func<CancellationToken, Task> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _tasks.Add(task);
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (_tasks.Count == 0) return;

        var tasks = _tasks.ToList();
        _tasks.Clear();

        _logger.LogDebug("Executing {Count} post-commit tasks", tasks.Count);

        for (var i = 0; i < tasks.Count; i++)
        {
            try
            {
                await tasks[i](cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Post-commit task {Index}/{Total} failed", i + 1, tasks.Count);
            }
        }
    }
}
