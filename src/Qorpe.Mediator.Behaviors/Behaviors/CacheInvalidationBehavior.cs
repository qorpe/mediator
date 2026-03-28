using Microsoft.Extensions.Logging;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Attributes;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that invalidates cache entries after successful command execution.
/// Triggered by [InvalidatesCache("prefix")] attribute on command types.
/// Executes after the handler succeeds — never invalidates on failure.
/// </summary>
public sealed class CacheInvalidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 1001; // Just after CachingBehavior (1000)
    private readonly ICacheInvalidator? _cacheInvalidator;
    private readonly ILogger<CacheInvalidationBehavior<TRequest, TResponse>> _logger;

    public CacheInvalidationBehavior(
        ILogger<CacheInvalidationBehavior<TRequest, TResponse>> logger,
        ICacheInvalidator? cacheInvalidator = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheInvalidator = cacheInvalidator;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var invalidateAttrs = typeof(TRequest).GetCustomAttributes(typeof(InvalidatesCacheAttribute), true)
            as InvalidatesCacheAttribute[];

        if (invalidateAttrs is null || invalidateAttrs.Length == 0 || _cacheInvalidator is null)
        {
            return await next().ConfigureAwait(false);
        }

        // Execute the handler first
        var response = await next().ConfigureAwait(false);

        // Invalidate cache entries after successful execution
        for (int i = 0; i < invalidateAttrs.Length; i++)
        {
            try
            {
                await _cacheInvalidator.InvalidateAsync(invalidateAttrs[i].KeyPrefix, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Invalidated cache entries with prefix '{Prefix}' after {RequestName}",
                    invalidateAttrs[i].KeyPrefix, typeof(TRequest).Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Cache invalidation failed for prefix '{Prefix}' after {RequestName}",
                    invalidateAttrs[i].KeyPrefix, typeof(TRequest).Name);
            }
        }

        return response;
    }
}
