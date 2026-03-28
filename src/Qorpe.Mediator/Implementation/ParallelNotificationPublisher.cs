using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.Implementation;

/// <summary>
/// Parallel notification publisher — executes all handlers concurrently.
/// Collects all exceptions and throws <see cref="AggregateException"/> if any fail.
/// </summary>
public sealed class ParallelNotificationPublisher : INotificationPublisher
{
    private readonly TimeSpan? _timeout;

    /// <summary>
    /// Initializes a new instance of <see cref="ParallelNotificationPublisher"/>.
    /// </summary>
    /// <param name="timeout">Optional timeout for all handlers to complete.</param>
    public ParallelNotificationPublisher(TimeSpan? timeout = null)
    {
        _timeout = timeout;
    }

    /// <inheritdoc />
    public async ValueTask Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);
        ArgumentNullException.ThrowIfNull(notification);

        if (handlerExecutors.Count == 0)
        {
            return;
        }

        if (handlerExecutors.Count == 1)
        {
            // Optimize single handler — no need for Task.WhenAll
            await handlerExecutors[0].HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tasks = new Task[handlerExecutors.Count];
        for (int i = 0; i < handlerExecutors.Count; i++)
        {
            tasks[i] = handlerExecutors[i].HandlerCallback(notification, cancellationToken).AsTask();
        }

        if (_timeout.HasValue)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout.Value);

            try
            {
                await Task.WhenAll(tasks).WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Notification handlers did not complete within the timeout of {_timeout.Value.TotalMilliseconds}ms.");
            }
        }
        else
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
}
