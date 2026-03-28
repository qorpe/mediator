using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Exceptions;

namespace Qorpe.Mediator.Implementation;

/// <summary>
/// Abstract base for type-erased request handler invocation.
/// One concrete wrapper is created per (TRequest, TResponse) pair and cached forever.
/// This eliminates ALL reflection from the hot path.
/// </summary>
internal abstract class RequestHandlerWrapperBase
{
    public abstract ValueTask<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

/// <summary>
/// Typed request handler wrapper. Resolves handler and behaviors from DI with full type safety.
/// Zero reflection, zero boxing on the typed path.
/// </summary>
internal sealed class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerWrapperBase
    where TRequest : IRequest<TResponse>
{
    public override async ValueTask<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var result = await HandleTyped((TRequest)request, serviceProvider, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public ValueTask<TResponse> HandleTyped(TRequest request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        // Resolve handler — fully typed, no reflection
        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();
        if (handler is null)
        {
            throw new HandlerNotFoundException(typeof(TRequest));
        }

        // Resolve behaviors — fully typed, no reflection, no MakeGenericType
        var behaviors = serviceProvider.GetService<IEnumerable<IPipelineBehavior<TRequest, TResponse>>>();

        // Fast path: no behaviors — direct handler call
        if (behaviors is null)
        {
            return handler.Handle(request, cancellationToken);
        }

        // Materialize behaviors (avoid multiple enumeration)
        IPipelineBehavior<TRequest, TResponse>[]? behaviorArray = null;
        int behaviorCount = 0;

        if (behaviors is IPipelineBehavior<TRequest, TResponse>[] arr)
        {
            behaviorArray = arr;
            behaviorCount = arr.Length;
        }
        else if (behaviors is ICollection<IPipelineBehavior<TRequest, TResponse>> col)
        {
            behaviorCount = col.Count;
            if (behaviorCount > 0)
            {
                behaviorArray = new IPipelineBehavior<TRequest, TResponse>[behaviorCount];
                col.CopyTo(behaviorArray, 0);
            }
        }
        else
        {
            // Fallback: enumerate once
            var list = new List<IPipelineBehavior<TRequest, TResponse>>();
            foreach (var b in behaviors)
            {
                list.Add(b);
            }
            behaviorArray = list.ToArray();
            behaviorCount = behaviorArray.Length;
        }

        if (behaviorCount == 0)
        {
            return handler.Handle(request, cancellationToken);
        }

        // Build pipeline chain — fully typed, no MethodInfo.Invoke, no object[] allocation
        RequestHandlerDelegate<TResponse> next = () => handler.Handle(request, cancellationToken);

        for (int i = behaviorCount - 1; i >= 0; i--)
        {
            var behavior = behaviorArray![i];
            var currentNext = next;
            next = () => behavior.Handle(request, currentNext, cancellationToken);
        }

        return next();
    }
}

/// <summary>
/// Abstract base for type-erased notification handler invocation.
/// </summary>
internal abstract class NotificationHandlerWrapperBase
{
    public abstract ValueTask Handle(INotification notification, IServiceProvider serviceProvider, CancellationToken cancellationToken,
        INotificationPublisher publisher);
}

/// <summary>
/// Typed notification handler wrapper. Zero reflection on the hot path.
/// Caches a static callback delegate per TNotification to avoid closure allocation.
/// </summary>
internal sealed class NotificationHandlerWrapper<TNotification> : NotificationHandlerWrapperBase
    where TNotification : INotification
{
    // Cached static callback — avoids closure allocation per handler per call
    private static readonly Func<INotification, CancellationToken, ValueTask>[] EmptyCallbacks = Array.Empty<Func<INotification, CancellationToken, ValueTask>>();

    public override ValueTask Handle(INotification notification, IServiceProvider serviceProvider, CancellationToken cancellationToken,
        INotificationPublisher publisher)
    {
        // Resolve handlers — fully typed, single DI call
        var handlers = serviceProvider.GetService<IEnumerable<INotificationHandler<TNotification>>>();

        if (handlers is null)
        {
            return ValueTask.CompletedTask;
        }

        // Fast path: check if ICollection to pre-allocate exact size
        int capacity;
        if (handlers is ICollection<INotificationHandler<TNotification>> col)
        {
            capacity = col.Count;
            if (capacity == 0) return ValueTask.CompletedTask;
        }
        else
        {
            capacity = 4;
        }

        var executors = new NotificationHandlerExecutor[capacity];
        int count = 0;

        foreach (var handler in handlers)
        {
            if (count >= executors.Length)
            {
                var newArr = new NotificationHandlerExecutor[executors.Length * 2];
                Array.Copy(executors, newArr, count);
                executors = newArr;
            }
            // Use a static method reference to avoid closure allocation
            var h = handler;
            executors[count++] = new NotificationHandlerExecutor(h, CreateCallback(h));
        }

        if (count == 0)
        {
            return ValueTask.CompletedTask;
        }

        // Use ArraySegment-style read if oversized
        if (count < executors.Length)
        {
            Array.Resize(ref executors, count);
        }

        return publisher.Publish(executors, notification, cancellationToken);
    }

    private static Func<INotification, CancellationToken, ValueTask> CreateCallback(INotificationHandler<TNotification> handler)
    {
        // Single closure per handler — captures only the handler instance
        return (n, ct) => handler.Handle((TNotification)n, ct);
    }
}

/// <summary>
/// Abstract base for type-erased stream handler invocation.
/// </summary>
internal abstract class StreamHandlerWrapperBase
{
    public abstract IAsyncEnumerable<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

/// <summary>
/// Typed stream handler wrapper. Zero reflection.
/// </summary>
internal sealed class StreamHandlerWrapper<TRequest, TResponse> : StreamHandlerWrapperBase
    where TRequest : IStreamRequest<TResponse>
{
    public override async IAsyncEnumerable<object?> Handle(object request, IServiceProvider serviceProvider,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetService<IStreamRequestHandler<TRequest, TResponse>>();
        if (handler is null)
        {
            throw new HandlerNotFoundException(typeof(TRequest));
        }

        await foreach (var item in handler.Handle((TRequest)request, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}

/// <summary>
/// Cached delegate for typed Send invocation, avoiding boxing through the abstract base.
/// </summary>
internal delegate ValueTask<TResponse> TypedSendDelegate<TResponse>(object request, IServiceProvider sp, CancellationToken ct);

/// <summary>
/// Factory and cache for handler wrappers. Creates wrappers once per type, caches forever.
/// The only place MakeGenericType is called — and only on first access.
/// </summary>
internal static class HandlerWrapperFactory
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapperBase> RequestWrappers = new();
    private static readonly ConcurrentDictionary<Type, NotificationHandlerWrapperBase> NotificationWrappers = new();
    private static readonly ConcurrentDictionary<Type, StreamHandlerWrapperBase> StreamWrappers = new();

    // Cache typed send delegates to avoid going through the abstract base (no boxing)
    private static readonly ConcurrentDictionary<Type, object> TypedSendDelegates = new();

    public static RequestHandlerWrapperBase GetRequestWrapper(Type requestType, Type responseType)
    {
        return RequestWrappers.GetOrAdd(requestType, static (type, resp) =>
        {
            var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(type, resp);
            return (RequestHandlerWrapperBase)Activator.CreateInstance(wrapperType)!;
        }, responseType);
    }

    /// <summary>
    /// Gets a typed send delegate that bypasses the abstract base, avoiding object boxing.
    /// </summary>
    public static TypedSendDelegate<TResponse> GetTypedSendDelegate<TResponse>(Type requestType)
    {
        var cached = TypedSendDelegates.GetOrAdd(requestType, static (type, resp) =>
        {
            var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(type, resp);
            var wrapper = Activator.CreateInstance(wrapperType)!;
            var handleMethod = wrapperType.GetMethod("HandleTyped")!;

            // Create a delegate that calls HandleTyped directly
            return new TypedSendDelegate<TResponse>((req, sp, ct) =>
            {
                var result = handleMethod.Invoke(wrapper, new[] { req, sp, (object)ct });
                return (ValueTask<TResponse>)result!;
            });
        }, typeof(TResponse));

        return (TypedSendDelegate<TResponse>)cached;
    }

    public static NotificationHandlerWrapperBase GetNotificationWrapper(Type notificationType)
    {
        return NotificationWrappers.GetOrAdd(notificationType, static type =>
        {
            var wrapperType = typeof(NotificationHandlerWrapper<>).MakeGenericType(type);
            return (NotificationHandlerWrapperBase)Activator.CreateInstance(wrapperType)!;
        });
    }

    public static StreamHandlerWrapperBase GetStreamWrapper(Type requestType, Type responseType)
    {
        return StreamWrappers.GetOrAdd(requestType, static (type, resp) =>
        {
            var wrapperType = typeof(StreamHandlerWrapper<,>).MakeGenericType(type, resp);
            return (StreamHandlerWrapperBase)Activator.CreateInstance(wrapperType)!;
        }, responseType);
    }

    internal static void ClearCache()
    {
        RequestWrappers.Clear();
        NotificationWrappers.Clear();
        StreamWrappers.Clear();
        TypedSendDelegates.Clear();
    }
}
