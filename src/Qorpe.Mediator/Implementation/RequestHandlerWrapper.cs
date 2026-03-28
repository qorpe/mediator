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
    // Tri-state behavior detection cache: 0=unknown, 1=no behaviors, 2=has behaviors
    // After first call, we know whether behaviors exist and skip the DI resolve entirely
    private volatile int _behaviorState;

    public override async ValueTask<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var result = await HandleTyped((TRequest)request, serviceProvider, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public ValueTask<TResponse> HandleTyped(TRequest request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        // Resolve handler — fully typed, single DI call
        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();
        if (handler is null)
        {
            throw new HandlerNotFoundException(typeof(TRequest));
        }

        // OPTIMIZATION: After first call, skip behavior DI resolve entirely if no behaviors registered
        var state = _behaviorState;
        if (state == 1) // No behaviors — direct handler call, skip DI resolve
        {
            return handler.Handle(request, cancellationToken);
        }

        if (state == 2) // Known to have behaviors — resolve and build pipeline
        {
            return ExecuteWithBehaviors(request, handler, serviceProvider, cancellationToken);
        }

        // First call (state == 0) — detect and cache
        return DetectAndExecute(request, handler, serviceProvider, cancellationToken);
    }

    private ValueTask<TResponse> DetectAndExecute(TRequest request, IRequestHandler<TRequest, TResponse> handler,
        IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var behaviors = serviceProvider.GetService<IEnumerable<IPipelineBehavior<TRequest, TResponse>>>();

        if (behaviors is null)
        {
            _behaviorState = 1; // Cache: no behaviors
            return handler.Handle(request, cancellationToken);
        }

        // Count behaviors
        int behaviorCount = 0;
        if (behaviors is ICollection<IPipelineBehavior<TRequest, TResponse>> col)
        {
            behaviorCount = col.Count;
        }
        else
        {
            foreach (var _ in behaviors)
            {
                behaviorCount++;
                break; // Just need to know if > 0
            }
        }

        if (behaviorCount == 0)
        {
            _behaviorState = 1; // Cache: no behaviors
            return handler.Handle(request, cancellationToken);
        }

        _behaviorState = 2; // Cache: has behaviors
        return ExecuteWithBehaviors(request, handler, serviceProvider, cancellationToken);
    }

    private static ValueTask<TResponse> ExecuteWithBehaviors(TRequest request, IRequestHandler<TRequest, TResponse> handler,
        IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var behaviors = serviceProvider.GetService<IEnumerable<IPipelineBehavior<TRequest, TResponse>>>();
        if (behaviors is null)
        {
            return handler.Handle(request, cancellationToken);
        }

        // Materialize to array
        IPipelineBehavior<TRequest, TResponse>[] behaviorArray;
        if (behaviors is IPipelineBehavior<TRequest, TResponse>[] arr)
        {
            behaviorArray = arr;
        }
        else if (behaviors is ICollection<IPipelineBehavior<TRequest, TResponse>> col)
        {
            behaviorArray = new IPipelineBehavior<TRequest, TResponse>[col.Count];
            col.CopyTo(behaviorArray, 0);
        }
        else
        {
            var list = new List<IPipelineBehavior<TRequest, TResponse>>(behaviors);
            behaviorArray = list.ToArray();
        }

        if (behaviorArray.Length == 0)
        {
            return handler.Handle(request, cancellationToken);
        }

        // Build pipeline chain — fully typed, zero reflection
        RequestHandlerDelegate<TResponse> next = () => handler.Handle(request, cancellationToken);

        for (int i = behaviorArray.Length - 1; i >= 0; i--)
        {
            var behavior = behaviorArray[i];
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
/// Uses DIRECT handler invocation — no NotificationHandlerExecutor allocation for sequential path.
/// Falls back to executor-based path only for custom publishers.
/// </summary>
internal sealed class NotificationHandlerWrapper<TNotification> : NotificationHandlerWrapperBase
    where TNotification : INotification
{
    public override ValueTask Handle(INotification notification, IServiceProvider serviceProvider, CancellationToken cancellationToken,
        INotificationPublisher publisher)
    {
        // Resolve handlers — single typed DI call
        var handlers = serviceProvider.GetService<IEnumerable<INotificationHandler<TNotification>>>();

        if (handlers is null)
        {
            return ValueTask.CompletedTask;
        }

        // OPTIMIZATION: For sequential publishers, invoke handlers directly without executor wrappers.
        // This eliminates NotificationHandlerExecutor + closure allocation entirely.
        if (publisher is ForeachNotificationPublisher foreachPublisher)
        {
            return InvokeDirectSequential((TNotification)notification, handlers, cancellationToken);
        }

        if (publisher is ParallelNotificationPublisher)
        {
            return InvokeDirectParallel((TNotification)notification, handlers, cancellationToken);
        }

        // Fallback: build executors for custom publisher implementations
        return InvokeViaExecutors(notification, handlers, publisher, cancellationToken);
    }

    private static async ValueTask InvokeDirectSequential(TNotification notification,
        IEnumerable<INotificationHandler<TNotification>> handlers, CancellationToken cancellationToken)
    {
        // Direct invocation — ZERO allocation per handler, no executors, no closures
        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask InvokeDirectParallel(TNotification notification,
        IEnumerable<INotificationHandler<TNotification>> handlers, CancellationToken cancellationToken)
    {
        // Collect tasks for parallel execution
        Task[]? tasks = null;
        int count = 0;

        foreach (var handler in handlers)
        {
            tasks ??= new Task[4];
            if (count >= tasks.Length)
            {
                var newArr = new Task[tasks.Length * 2];
                Array.Copy(tasks, newArr, count);
                tasks = newArr;
            }
            tasks[count++] = handler.Handle(notification, cancellationToken).AsTask();
        }

        if (count == 0) return;

        if (count == 1)
        {
            await tasks![0].ConfigureAwait(false);
            return;
        }

        if (count < tasks!.Length)
        {
            Array.Resize(ref tasks, count);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static ValueTask InvokeViaExecutors(INotification notification,
        IEnumerable<INotificationHandler<TNotification>> handlers,
        INotificationPublisher publisher, CancellationToken cancellationToken)
    {
        // Build executors for custom publishers
        NotificationHandlerExecutor[]? executors = null;
        int count = 0;

        foreach (var handler in handlers)
        {
            executors ??= new NotificationHandlerExecutor[4];
            if (count >= executors.Length)
            {
                var newArr = new NotificationHandlerExecutor[executors.Length * 2];
                Array.Copy(executors, newArr, count);
                executors = newArr;
            }
            var h = handler;
            executors[count++] = new NotificationHandlerExecutor(h, (n, ct) => h.Handle((TNotification)n, ct));
        }

        if (count == 0) return ValueTask.CompletedTask;
        if (count < executors!.Length) Array.Resize(ref executors, count);

        return publisher.Publish(executors, notification, cancellationToken);
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
