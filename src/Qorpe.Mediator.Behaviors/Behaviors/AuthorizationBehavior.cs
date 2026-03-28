using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Attributes;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that enforces authorization requirements.
/// Checks [Authorize] attributes — all must pass. No context = Unauthorized.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IAuthorizationContext? _authContext;
    private readonly ILogger<AuthorizationBehavior<TRequest, TResponse>> _logger;
    private readonly AuthorizationBehaviorOptions _options;

    public AuthorizationBehavior(
        ILogger<AuthorizationBehavior<TRequest, TResponse>> logger,
        IOptions<AuthorizationBehaviorOptions> options,
        IAuthorizationContext? authContext = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _authContext = authContext;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        var authorizeAttributes = typeof(TRequest)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ToArray();

        if (authorizeAttributes.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        // No auth context — unauthorized
        if (_authContext is null || !_authContext.IsAuthenticated)
        {
            _logger.LogWarning("Unauthorized access attempt for {RequestName}", typeof(TRequest).Name);
            return CreateUnauthorizedResult();
        }

        // Check all [Authorize] attributes — ALL must pass
        for (int i = 0; i < authorizeAttributes.Length; i++)
        {
            var attr = authorizeAttributes[i];

            if (attr.Roles is not null)
            {
                var requiredRoles = attr.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (int j = 0; j < requiredRoles.Length; j++)
                {
                    var found = false;
                    for (int k = 0; k < _authContext.Roles.Count; k++)
                    {
                        if (string.Equals(_authContext.Roles[k], requiredRoles[j], StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        _logger.LogWarning(
                            "Forbidden: user {UserId} lacks role '{Role}' for {RequestName}",
                            _authContext.UserId, requiredRoles[j], typeof(TRequest).Name);
                        return CreateForbiddenResult();
                    }
                }
            }

            if (attr.Policy is not null)
            {
                if (!_authContext.HasClaim("Policy", attr.Policy))
                {
                    _logger.LogWarning(
                        "Forbidden: user {UserId} does not satisfy policy '{Policy}' for {RequestName}",
                        _authContext.UserId, attr.Policy, typeof(TRequest).Name);
                    return CreateForbiddenResult();
                }
            }
        }

        return await next().ConfigureAwait(false);
    }

    private static TResponse CreateUnauthorizedResult()
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(Error.Unauthorized("Auth.Unauthorized", "Authentication is required."));
        }

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var error = Error.Unauthorized("Auth.Unauthorized", "Authentication is required.");
            var failureMethod = typeof(TResponse).GetMethod("Failure", new[] { typeof(Error) });
            return (TResponse)failureMethod!.Invoke(null, new object[] { error })!;
        }

        throw new UnauthorizedAccessException("Authentication is required.");
    }

    private static TResponse CreateForbiddenResult()
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(Error.Forbidden("Auth.Forbidden", "You do not have permission to perform this action."));
        }

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var error = Error.Forbidden("Auth.Forbidden", "You do not have permission to perform this action.");
            var failureMethod = typeof(TResponse).GetMethod("Failure", new[] { typeof(Error) });
            return (TResponse)failureMethod!.Invoke(null, new object[] { error })!;
        }

        throw new UnauthorizedAccessException("You do not have permission to perform this action.");
    }
}
