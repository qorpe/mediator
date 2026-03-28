using System.Collections.Concurrent;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Exceptions;

namespace Qorpe.Mediator.Implementation;

/// <summary>
/// Builds and caches the pipeline chain for each request type.
/// One pipeline per request type — built once, reused forever.
/// All type computations (MakeGenericType) are cached on first call.
/// </summary>
internal static class RequestPipeline
{
    // Cache: RequestType -> (handlerInterfaceType, behaviorInterfaceType, enumerableBehaviorType)
    private static readonly ConcurrentDictionary<Type, PipelineTypeInfo> TypeInfoCache = new();

    /// <summary>
    /// Executes the pipeline for the given request.
    /// </summary>
    internal static ValueTask<TResponse> Execute<TResponse>(
        IRequest<TResponse> request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = request.GetType();

        // Get or build cached type info (MakeGenericType happens only once per request type)
        var typeInfo = TypeInfoCache.GetOrAdd(requestType, static type =>
        {
            var responseType = FindResponseType(type);
            var handlerType = typeof(IRequestHandler<,>).MakeGenericType(type, responseType);
            var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(type, responseType);
            var enumerableBehaviorType = typeof(IEnumerable<>).MakeGenericType(behaviorType);
            return new PipelineTypeInfo(handlerType, behaviorType, enumerableBehaviorType);
        });

        // Resolve handler — single GetService call
        var handler = serviceProvider.GetService(typeInfo.HandlerInterfaceType);
        if (handler is null)
        {
            throw new HandlerNotFoundException(requestType);
        }

        // Get compiled handler delegate (cached)
        var handlerDelegate = HandlerResolver.ResolveHandler<TResponse>(requestType, serviceProvider);

        // Get behaviors — one GetService call
        var behaviorsEnumerable = serviceProvider.GetService(typeInfo.EnumerableBehaviorType) as System.Collections.IEnumerable;

        // Fast path: no behaviors
        if (behaviorsEnumerable is null)
        {
            return handlerDelegate(serviceProvider, request, cancellationToken);
        }

        // Collect behaviors without List allocation when possible
        object[]? behaviors = null;
        int count = 0;

        foreach (var b in behaviorsEnumerable)
        {
            if (b is not null)
            {
                behaviors ??= new object[4];
                if (count >= behaviors.Length)
                {
                    var newArr = new object[behaviors.Length * 2];
                    Array.Copy(behaviors, newArr, count);
                    behaviors = newArr;
                }
                behaviors[count++] = b;
            }
        }

        if (count == 0)
        {
            return handlerDelegate(serviceProvider, request, cancellationToken);
        }

        // Build the pipeline chain from inside out
        RequestHandlerDelegate<TResponse> next = () => handlerDelegate(serviceProvider, request, cancellationToken);

        // Get the cached behavior invoker (MethodInfo.Invoke cached per type)
        var invoker = GetBehaviorInvoker<TResponse>(requestType);

        for (int i = count - 1; i >= 0; i--)
        {
            var capturedBehavior = behaviors![i];
            var currentNext = next;
            next = () => invoker(capturedBehavior, request, currentNext, cancellationToken);
        }

        return next();
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

    private static readonly ConcurrentDictionary<Type, object> BehaviorInvokerCache = new();

    private static Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, ValueTask<TResponse>>
        GetBehaviorInvoker<TResponse>(Type requestType)
    {
        var invoker = BehaviorInvokerCache.GetOrAdd(requestType, static reqType =>
        {
            var responseType = FindResponseType(reqType);
            var pipelineBehaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(reqType, responseType);
            var handleMethod = pipelineBehaviorType.GetMethod("Handle")!;

            return new Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, ValueTask<TResponse>>(
                (behavior, request, nextDelegate, ct) =>
                {
                    var result = handleMethod.Invoke(behavior, new object[] { request, nextDelegate, ct });
                    return (ValueTask<TResponse>)result!;
                });
        });

        return (Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, ValueTask<TResponse>>)invoker;
    }

    /// <summary>
    /// Clears the pipeline cache. For testing purposes only.
    /// </summary>
    internal static void ClearCache()
    {
        TypeInfoCache.Clear();
        BehaviorInvokerCache.Clear();
    }

    private sealed class PipelineTypeInfo
    {
        public Type HandlerInterfaceType { get; }
        public Type BehaviorInterfaceType { get; }
        public Type EnumerableBehaviorType { get; }

        public PipelineTypeInfo(Type handlerInterfaceType, Type behaviorInterfaceType, Type enumerableBehaviorType)
        {
            HandlerInterfaceType = handlerInterfaceType;
            BehaviorInterfaceType = behaviorInterfaceType;
            EnumerableBehaviorType = enumerableBehaviorType;
        }
    }
}
