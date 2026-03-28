using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Attributes;
using Qorpe.Mediator.Behaviors.Configuration;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that prevents duplicate command execution using idempotency keys.
/// Queries are automatically skipped. Concurrent requests: first executes, second waits.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IIdempotencyStore? _store;
    private readonly ILogger<IdempotencyBehavior<TRequest, TResponse>> _logger;
    private readonly IdempotencyBehaviorOptions _options;
    private static readonly SemaphoreSlim KeyLock = new(1, 1);

    public IdempotencyBehavior(
        ILogger<IdempotencyBehavior<TRequest, TResponse>> logger,
        IOptions<IdempotencyBehaviorOptions> options,
        IIdempotencyStore? store = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _store = store;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        // Skip queries
        if (IsQuery())
        {
            return await next().ConfigureAwait(false);
        }

        var idempotentAttr = typeof(TRequest).GetCustomAttributes(typeof(IdempotentAttribute), true)
            .Cast<IdempotentAttribute>()
            .FirstOrDefault();

        if (idempotentAttr is null)
        {
            return await next().ConfigureAwait(false);
        }

        // Window 0 means no check
        if (idempotentAttr.WindowSeconds <= 0)
        {
            return await next().ConfigureAwait(false);
        }

        // Store down — execute normally
        if (_store is null)
        {
            _logger.LogWarning("IIdempotencyStore not configured, executing request without idempotency check");
            return await next().ConfigureAwait(false);
        }

        var idempotencyKey = GenerateKey(request);
        var window = TimeSpan.FromSeconds(idempotentAttr.WindowSeconds);

        try
        {
            // Check if already processed
            if (await _store.ExistsAsync(idempotencyKey, cancellationToken).ConfigureAwait(false))
            {
                var cached = await _store.GetAsync<TResponse>(idempotencyKey, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                {
                    _logger.LogInformation("Idempotent request {RequestName} with key {Key} returned cached result",
                        typeof(TRequest).Name, idempotencyKey);
                    return cached;
                }
            }

            // Execute the request
            var response = await next().ConfigureAwait(false);

            // Store the result
            await _store.SetAsync(idempotencyKey, response, window, cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // First fails — don't cache
            _logger.LogWarning(ex, "Idempotent request {RequestName} failed, not caching", typeof(TRequest).Name);
            await _store.RemoveAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static string GenerateKey(TRequest request)
    {
        var typeName = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var json = JsonSerializer.Serialize(request);
        var combined = $"{typeName}:{json}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash);
    }

    private static bool IsQuery()
    {
        return typeof(TRequest).GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
    }
}
