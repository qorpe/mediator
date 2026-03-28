using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Attributes;
using Qorpe.Mediator.Behaviors.Configuration;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that wraps command execution in a transaction.
/// Automatically skips queries. Supports rollback and nested savepoints.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork? _unitOfWork;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;
    private readonly TransactionBehaviorOptions _options;

    public TransactionBehavior(
        ILogger<TransactionBehavior<TRequest, TResponse>> logger,
        IOptions<TransactionBehaviorOptions> options,
        IUnitOfWork? unitOfWork = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _unitOfWork = unitOfWork;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        // Skip queries — transactions are for commands only
        if (IsQuery())
        {
            return await next().ConfigureAwait(false);
        }

        // Check [Transactional] attribute or if it's a command (implicit transaction)
        var transactionalAttr = typeof(TRequest).GetCustomAttributes(typeof(TransactionalAttribute), true)
            .Cast<TransactionalAttribute>()
            .FirstOrDefault();

        if (transactionalAttr is null)
        {
            return await next().ConfigureAwait(false);
        }

        if (_unitOfWork is null)
        {
            throw new InvalidOperationException(
                $"IUnitOfWork is required for transactional request '{typeof(TRequest).Name}'. " +
                "Register an IUnitOfWork implementation in the DI container.");
        }

        var requestName = typeof(TRequest).Name;
        _logger.LogDebug("Beginning transaction for {RequestName}", requestName);

        await _unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        TResponse response;

        try
        {
            response = await next().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler failed for {RequestName}, rolling back transaction", requestName);
            await SafeRollbackAsync(requestName, cancellationToken).ConfigureAwait(false);
            throw;
        }

        try
        {
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Committed transaction for {RequestName}", requestName);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction commit failed for {RequestName}, rolling back", requestName);
            await SafeRollbackAsync(requestName, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask SafeRollbackAsync(string requestName, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork!.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogCritical(rollbackEx,
                "CRITICAL: Rollback failed for {RequestName}. Original exception preserved.",
                requestName);
        }
    }

    private static bool IsQuery()
    {
        return typeof(TRequest).GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
    }
}
