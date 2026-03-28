using System.Collections.Concurrent;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Exceptions;

namespace Qorpe.Mediator.Implementation;

/// <summary>
/// Default mediator implementation using typed handler wrappers.
/// Zero reflection on the hot path after first call per type.
/// Thread-safe and re-entrant — handler calling Send() inside will not deadlock.
/// </summary>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationPublisher _notificationPublisher;

    // Cache: requestType -> Func that does typed Send without boxing
    private static readonly ConcurrentDictionary<Type, object> SendDelegateCache = new();

    /// <summary>
    /// Initializes a new instance of <see cref="Mediator"/>.
    /// </summary>
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

        var requestType = request.GetType();

        // Get cached typed send delegate — one dictionary lookup, then direct typed call
        var sendDelegate = (Func<object, IServiceProvider, CancellationToken, ValueTask<TResponse>>)
            SendDelegateCache.GetOrAdd(requestType, static type =>
            {
                var responseType = FindResponseType(type);
                var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(type, responseType);
                var wrapper = Activator.CreateInstance(wrapperType)!;

                // Build a Func that directly calls the typed HandleTyped method
                // This uses a lambda that captures the typed wrapper — no reflection on invocation
                var method = wrapperType.GetMethod("HandleTyped")!;

                return CreateSendDelegate<TResponse>(wrapper, method);
            });

        return sendDelegate(request, _serviceProvider, cancellationToken);
    }

    private static object CreateSendDelegate<TResponse>(object wrapper, System.Reflection.MethodInfo handleMethod)
    {
        // Compile an Expression Tree delegate for zero-reflection invocation.
        // This creates: (object req, IServiceProvider sp, CancellationToken ct) =>
        //     ((RequestHandlerWrapper<TReq, TResp>)wrapper).HandleTyped((TReq)req, sp, ct)
        var reqParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "req");
        var spParam = System.Linq.Expressions.Expression.Parameter(typeof(IServiceProvider), "sp");
        var ctParam = System.Linq.Expressions.Expression.Parameter(typeof(CancellationToken), "ct");

        var wrapperConst = System.Linq.Expressions.Expression.Constant(wrapper);
        var requestType = handleMethod.GetParameters()[0].ParameterType;
        var castReq = System.Linq.Expressions.Expression.Convert(reqParam, requestType);

        var call = System.Linq.Expressions.Expression.Call(wrapperConst, handleMethod, castReq, spParam, ctParam);

        var lambda = System.Linq.Expressions.Expression.Lambda<Func<object, IServiceProvider, CancellationToken, ValueTask<TResponse>>>(
            call, reqParam, spParam, ctParam);

        return lambda.Compile();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = request.GetType();
        var wrapper = HandlerWrapperFactory.GetStreamWrapper(requestType, typeof(TResponse));

        return StreamResults<TResponse>(wrapper, request, cancellationToken);
    }

    private async IAsyncEnumerable<TResponse> StreamResults<TResponse>(
        StreamHandlerWrapperBase wrapper, object request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in wrapper.Handle(request, _serviceProvider, cancellationToken).ConfigureAwait(false))
        {
            yield return (TResponse)item!;
        }
    }

    /// <inheritdoc />
    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        var wrapper = HandlerWrapperFactory.GetNotificationWrapper(typeof(TNotification));
        return wrapper.Handle(notification, _serviceProvider, cancellationToken, _notificationPublisher);
    }

    /// <inheritdoc />
    public ValueTask Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        var notificationType = notification.GetType();
        var wrapper = HandlerWrapperFactory.GetNotificationWrapper(notificationType);
        return wrapper.Handle(notification, _serviceProvider, cancellationToken, _notificationPublisher);
    }

    private static Type FindResponseType(Type requestType)
    {
        var interfaces = requestType.GetInterfaces();
        for (int i = 0; i < interfaces.Length; i++)
        {
            var iface = interfaces[i];
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IRequest<>))
            {
                return iface.GetGenericArguments()[0];
            }
        }
        throw new HandlerNotFoundException(requestType);
    }

    /// <summary>
    /// Clears all caches. For testing purposes only.
    /// </summary>
    internal static void ClearAllCaches()
    {
        SendDelegateCache.Clear();
        HandlerWrapperFactory.ClearCache();
        HandlerResolver.ClearCache();
        RequestPipeline.ClearCache();
    }
}
