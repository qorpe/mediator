using System.Buffers;
using System.Collections.Concurrent;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Exceptions;

namespace Qorpe.Mediator.Implementation;

/// <summary>
/// Builds and caches the pipeline chain for each request type.
/// One pipeline per request type — built once, reused forever.
/// </summary>
internal static class RequestPipeline
{
    private static readonly ConcurrentDictionary<Type, object> PipelineCache = new();

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
        var handlerDelegate = HandlerResolver.ResolveHandler<TResponse>(requestType, serviceProvider);

        // Check handler exists
        var handlerInterfaceType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = serviceProvider.GetService(handlerInterfaceType);
        if (handler is null)
        {
            throw new HandlerNotFoundException(requestType);
        }

        // Get behaviors
        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviors = GetBehaviors(serviceProvider, behaviorType);

        if (behaviors.Length == 0)
        {
            // No behaviors — direct handler call, zero overhead
            return handlerDelegate(serviceProvider, request, cancellationToken);
        }

        // Build the pipeline chain from inside out
        RequestHandlerDelegate<TResponse> next = () => handlerDelegate(serviceProvider, request, cancellationToken);

        // Iterate behaviors in reverse order to build the chain
        for (int i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var currentNext = next; // Capture for closure

            // Use the cached invoke delegate
            var invoker = GetBehaviorInvoker<TResponse>(requestType, behaviorType);
            var capturedBehavior = behavior;
            next = () => invoker(capturedBehavior, request, currentNext, cancellationToken);
        }

        return next();
    }

    private static object[] GetBehaviors(IServiceProvider serviceProvider, Type behaviorType)
    {
        // Use IEnumerable<IPipelineBehavior<TRequest, TResponse>> from DI
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(behaviorType);
        var behaviors = serviceProvider.GetService(enumerableType) as System.Collections.IEnumerable;

        if (behaviors is null)
        {
            return Array.Empty<object>();
        }

        var list = new List<object>();
        foreach (var b in behaviors)
        {
            if (b is not null)
            {
                list.Add(b);
            }
        }

        return list.ToArray();
    }

    private static readonly ConcurrentDictionary<(Type, Type), object> BehaviorInvokerCache = new();

    private static Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, ValueTask<TResponse>>
        GetBehaviorInvoker<TResponse>(Type requestType, Type behaviorType)
    {
        var key = (requestType, typeof(TResponse));
        var invoker = BehaviorInvokerCache.GetOrAdd(key, static k =>
        {
            var (reqType, respType) = k;
            var pipelineBehaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(reqType, respType);
            var handleMethod = pipelineBehaviorType.GetMethod("Handle")!;

            return new Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, ValueTask<TResponse>>(
                (behavior, request, nextDelegate, ct) =>
                {
                    // Dynamic invoke through the interface
                    var typedBehavior = behavior;
                    var result = handleMethod.Invoke(typedBehavior, new object[] { request, nextDelegate, ct });
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
        PipelineCache.Clear();
        BehaviorInvokerCache.Clear();
    }
}
