using System.Collections.Concurrent;
using System.Linq.Expressions;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Exceptions;

namespace Qorpe.Mediator.Implementation;

/// <summary>
/// Resolves and caches handler delegates for request types using compiled expressions.
/// Uses generic static field caching for maximum performance.
/// </summary>
internal static class HandlerResolver
{
    private static readonly ConcurrentDictionary<Type, object> HandlerDelegateCache = new();
    private static readonly ConcurrentDictionary<Type, object> StreamHandlerDelegateCache = new();
    private static readonly ConcurrentDictionary<Type, Type> RequestToHandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Type> StreamRequestToHandlerTypeCache = new();

    /// <summary>
    /// Resolves the handler for a request type and returns a compiled delegate for invocation.
    /// </summary>
    internal static Func<IServiceProvider, object, CancellationToken, ValueTask<TResponse>> ResolveHandler<TResponse>(
        Type requestType,
        IServiceProvider serviceProvider)
    {
        var cachedDelegate = HandlerDelegateCache.GetOrAdd(requestType, static type =>
        {
            var handlerType = GetRequestHandlerType(type);
            return CompileHandlerDelegate<TResponse>(type, handlerType);
        });

        return (Func<IServiceProvider, object, CancellationToken, ValueTask<TResponse>>)cachedDelegate;
    }

    /// <summary>
    /// Resolves the stream handler for a streaming request type.
    /// </summary>
    internal static Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<TResponse>> ResolveStreamHandler<TResponse>(
        Type requestType,
        IServiceProvider serviceProvider)
    {
        var cachedDelegate = StreamHandlerDelegateCache.GetOrAdd(requestType, static type =>
        {
            var handlerType = GetStreamRequestHandlerType(type);
            return CompileStreamHandlerDelegate<TResponse>(type, handlerType);
        });

        return (Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<TResponse>>)cachedDelegate;
    }

    private static Type GetRequestHandlerType(Type requestType)
    {
        return RequestToHandlerTypeCache.GetOrAdd(requestType, static type =>
        {
            // Find the IRequest<TResponse> interface to determine TResponse
            var requestInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

            if (requestInterface is null)
            {
                throw new HandlerNotFoundException(type);
            }

            var responseType = requestInterface.GetGenericArguments()[0];
            return typeof(IRequestHandler<,>).MakeGenericType(type, responseType);
        });
    }

    private static Type GetStreamRequestHandlerType(Type requestType)
    {
        return StreamRequestToHandlerTypeCache.GetOrAdd(requestType, static type =>
        {
            var streamInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamRequest<>));

            if (streamInterface is null)
            {
                throw new HandlerNotFoundException(type);
            }

            var responseType = streamInterface.GetGenericArguments()[0];
            return typeof(IStreamRequestHandler<,>).MakeGenericType(type, responseType);
        });
    }

    private static Func<IServiceProvider, object, CancellationToken, ValueTask<TResponse>> CompileHandlerDelegate<TResponse>(
        Type requestType,
        Type handlerInterfaceType)
    {
        // Parameters: IServiceProvider sp, object request, CancellationToken ct
        var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
        var requestParam = Expression.Parameter(typeof(object), "request");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        // var handler = (IRequestHandler<TReq, TResp>)sp.GetService(handlerInterfaceType);
        var getServiceMethod = typeof(IServiceProvider).GetMethod(nameof(IServiceProvider.GetService))!;
        var getServiceCall = Expression.Call(spParam, getServiceMethod, Expression.Constant(handlerInterfaceType));
        var castedHandler = Expression.Convert(getServiceCall, handlerInterfaceType);

        // handler.Handle((TRequest)request, ct)
        var handleMethod = handlerInterfaceType.GetMethod("Handle")!;
        var castedRequest = Expression.Convert(requestParam, requestType);
        var handleCall = Expression.Call(castedHandler, handleMethod, castedRequest, ctParam);

        var lambda = Expression.Lambda<Func<IServiceProvider, object, CancellationToken, ValueTask<TResponse>>>(
            handleCall, spParam, requestParam, ctParam);

        return lambda.Compile();
    }

    private static Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<TResponse>> CompileStreamHandlerDelegate<TResponse>(
        Type requestType,
        Type handlerInterfaceType)
    {
        var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
        var requestParam = Expression.Parameter(typeof(object), "request");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var getServiceMethod = typeof(IServiceProvider).GetMethod(nameof(IServiceProvider.GetService))!;
        var getServiceCall = Expression.Call(spParam, getServiceMethod, Expression.Constant(handlerInterfaceType));
        var castedHandler = Expression.Convert(getServiceCall, handlerInterfaceType);

        var handleMethod = handlerInterfaceType.GetMethod("Handle")!;
        var castedRequest = Expression.Convert(requestParam, requestType);
        var handleCall = Expression.Call(castedHandler, handleMethod, castedRequest, ctParam);

        var lambda = Expression.Lambda<Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<TResponse>>>(
            handleCall, spParam, requestParam, ctParam);

        return lambda.Compile();
    }

    /// <summary>
    /// Clears all cached delegates. For testing purposes only.
    /// </summary>
    internal static void ClearCache()
    {
        HandlerDelegateCache.Clear();
        StreamHandlerDelegateCache.Clear();
        RequestToHandlerTypeCache.Clear();
        StreamRequestToHandlerTypeCache.Clear();
    }
}
