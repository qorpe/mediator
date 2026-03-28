using System.Collections.Concurrent;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Exceptions;

namespace Qorpe.Mediator.Implementation;

/// <summary>
/// Default mediator implementation with compiled delegate caching and cached pipelines.
/// Thread-safe and re-entrant — handler calling Send() inside will not deadlock.
/// </summary>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationPublisher _notificationPublisher;

    // Cache for notification handler executors per notification type
    private static readonly ConcurrentDictionary<Type, object> NotificationHandlerCache = new();

    /// <summary>
    /// Initializes a new instance of <see cref="Mediator"/>.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="notificationPublisher">The notification publisher strategy.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public Mediator(IServiceProvider serviceProvider, INotificationPublisher notificationPublisher)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _notificationPublisher = notificationPublisher ?? throw new ArgumentNullException(nameof(notificationPublisher));
    }

    /// <inheritdoc />
    public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return RequestPipeline.Execute(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = request.GetType();
        var handlerDelegate = HandlerResolver.ResolveStreamHandler<TResponse>(requestType, _serviceProvider);

        // Verify handler exists
        var handlerInterfaceType = typeof(IStreamRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerInterfaceType);
        if (handler is null)
        {
            throw new HandlerNotFoundException(requestType);
        }

        return handlerDelegate(_serviceProvider, request, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        return PublishInternal(notification, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        return PublishInternal(notification, cancellationToken);
    }

    private ValueTask PublishInternal(INotification notification, CancellationToken cancellationToken)
    {
        var notificationType = notification.GetType();
        var handlerExecutors = GetNotificationHandlerExecutors(notificationType);

        if (handlerExecutors.Count == 0)
        {
            // No handlers — silent success
            return ValueTask.CompletedTask;
        }

        return _notificationPublisher.Publish(handlerExecutors, notification, cancellationToken);
    }

    private IReadOnlyList<NotificationHandlerExecutor> GetNotificationHandlerExecutors(Type notificationType)
    {
        // Build the factory delegate for this notification type
        var factory = (Func<IServiceProvider, IReadOnlyList<NotificationHandlerExecutor>>)NotificationHandlerCache.GetOrAdd(
            notificationType,
            static type =>
            {
                var handlerInterfaceType = typeof(INotificationHandler<>).MakeGenericType(type);
                var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerInterfaceType);
                var handleMethod = handlerInterfaceType.GetMethod("Handle")!;

                return new Func<IServiceProvider, IReadOnlyList<NotificationHandlerExecutor>>(sp =>
                {
                    var handlers = sp.GetService(enumerableType) as System.Collections.IEnumerable;
                    if (handlers is null)
                    {
                        return Array.Empty<NotificationHandlerExecutor>();
                    }

                    var executors = new List<NotificationHandlerExecutor>();
                    foreach (var handler in handlers)
                    {
                        if (handler is null) continue;

                        var capturedHandler = handler;
                        executors.Add(new NotificationHandlerExecutor(
                            capturedHandler,
                            (notification, ct) =>
                            {
                                var result = handleMethod.Invoke(capturedHandler, new object[] { notification, ct });
                                return (ValueTask)result!;
                            }));
                    }

                    return executors;
                });
            });

        return factory(_serviceProvider);
    }

    /// <summary>
    /// Clears all caches. For testing purposes only.
    /// </summary>
    internal static void ClearAllCaches()
    {
        NotificationHandlerCache.Clear();
        HandlerResolver.ClearCache();
        RequestPipeline.ClearCache();
    }
}
