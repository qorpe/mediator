using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Configuration;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that monitors request execution time using Stopwatch.
/// Logs warnings for slow requests and critical alerts for very slow requests.
/// </summary>
public sealed class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly PerformanceBehaviorOptions _options;

    public PerformanceBehavior(
        ILogger<PerformanceBehavior<TRequest, TResponse>> logger,
        IOptions<PerformanceBehaviorOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (_options.WarningThresholdMs <= 0)
        {
            throw new ArgumentException(
                "WarningThresholdMs must be positive.", nameof(options));
        }
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        var sw = Stopwatch.StartNew();
        var response = await next().ConfigureAwait(false);
        sw.Stop();

        var elapsedMs = sw.Elapsed.TotalMilliseconds;

        // Under 0.01ms — don't log (too fast to be meaningful)
        if (elapsedMs < 0.01)
        {
            return response;
        }

        var requestName = typeof(TRequest).Name;

        // Over 30 seconds — critical
        if (elapsedMs > 30_000)
        {
            _logger.LogCritical(
                "CRITICAL: {RequestName} took {ElapsedMs}ms (over 30s threshold)",
                requestName, elapsedMs);
        }
        else if (elapsedMs > _options.CriticalThresholdMs)
        {
            _logger.LogError(
                "SLOW: {RequestName} took {ElapsedMs}ms (critical threshold: {Threshold}ms)",
                requestName, elapsedMs, _options.CriticalThresholdMs);
        }
        else if (elapsedMs > _options.WarningThresholdMs)
        {
            _logger.LogWarning(
                "SLOW: {RequestName} took {ElapsedMs}ms (warning threshold: {Threshold}ms)",
                requestName, elapsedMs, _options.WarningThresholdMs);
        }

        return response;
    }
}
