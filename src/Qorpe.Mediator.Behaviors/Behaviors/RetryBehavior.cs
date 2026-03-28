using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Attributes;
using Qorpe.Mediator.Behaviors.Configuration;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that retries requests on transient failures with exponential backoff and jitter.
/// Retries: TimeoutException, HttpRequestException, TaskCanceledException (non-user-cancelled).
/// Never retries: ValidationException, UnauthorizedAccessException, ArgumentException.
/// </summary>
public sealed class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 900;
    private readonly ILogger<RetryBehavior<TRequest, TResponse>> _logger;
    private readonly RetryBehaviorOptions _options;

    // Exception types that should never be retried
    private static readonly HashSet<Type> NonRetryableExceptions = new()
    {
        typeof(UnauthorizedAccessException),
        typeof(ArgumentException),
        typeof(ArgumentNullException),
        typeof(ArgumentOutOfRangeException),
        typeof(InvalidOperationException),
        typeof(NotSupportedException),
        typeof(NotImplementedException)
    };

    public RetryBehavior(
        ILogger<RetryBehavior<TRequest, TResponse>> logger,
        IOptions<RetryBehaviorOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        var retryAttr = typeof(TRequest).GetCustomAttributes(typeof(RetryableAttribute), true)
            .Cast<RetryableAttribute>()
            .FirstOrDefault();

        if (retryAttr is null)
        {
            return await next().ConfigureAwait(false);
        }

        var maxRetries = retryAttr.MaxRetryCount;

        // Max 0 — pass through
        if (maxRetries <= 0)
        {
            return await next().ConfigureAwait(false);
        }

        var initialDelayMs = retryAttr.InitialDelayMs;
        var useExponentialBackoff = retryAttr.UseExponentialBackoff;
        var maxBackoffMs = _options.MaxBackoffCapSeconds * 1000;

        var attempt = 0;
        while (true)
        {
            try
            {
                return await next().ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt, maxRetries, cancellationToken))
            {
                attempt++;
                var delay = CalculateDelay(attempt, initialDelayMs, useExponentialBackoff, maxBackoffMs);

                _logger.LogWarning(ex,
                    "Retry {Attempt}/{MaxRetries} for {RequestName} after {DelayMs}ms. Error: {Error}",
                    attempt, maxRetries, typeof(TRequest).Name, delay, ex.Message);

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cancel during wait — stop retrying
                    throw;
                }
            }
        }
    }

    private static bool ShouldRetry(Exception ex, int attempt, int maxRetries, CancellationToken cancellationToken)
    {
        if (attempt >= maxRetries)
        {
            return false;
        }

        // Never retry user-initiated cancellation
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        // Never retry non-retryable exceptions
        var exType = ex.GetType();
        if (NonRetryableExceptions.Contains(exType))
        {
            return false;
        }

        // Check for validation-type exceptions by name (FluentValidation etc.)
        if (exType.Name.Contains("Validation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Retryable: TimeoutException, HttpRequestException, TaskCanceledException (non-user)
        return ex is TimeoutException
            or HttpRequestException
            or (TaskCanceledException and not OperationCanceledException);
    }

    private static int CalculateDelay(int attempt, int initialDelayMs, bool useExponentialBackoff, int maxBackoffMs)
    {
        int baseDelay;
        if (useExponentialBackoff)
        {
            baseDelay = initialDelayMs * (1 << (attempt - 1)); // 2^(attempt-1)
        }
        else
        {
            baseDelay = initialDelayMs;
        }

        // Add jitter (±25%)
        var jitter = Random.Shared.Next(-baseDelay / 4, baseDelay / 4);
        var delay = Math.Max(0, baseDelay + jitter); // Ensure non-negative

        // Cap at max backoff
        return Math.Min(delay, maxBackoffMs);
    }
}
