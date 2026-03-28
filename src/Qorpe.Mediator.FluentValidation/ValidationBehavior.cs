using FluentValidation;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Results;

namespace Qorpe.Mediator.FluentValidation;

/// <summary>
/// Pipeline behavior that runs all FluentValidation validators for the request.
/// Multi-validator support: ALL validators run, all errors collected.
/// Returns Result.Failure with validation errors — no exceptions thrown for validation failures.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var validatorArray = _validators as IValidator<TRequest>[] ?? _validators.ToArray();

        if (validatorArray.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);
        var errors = new List<ValidationError>();

        // Run ALL validators — collect all errors
        for (int i = 0; i < validatorArray.Length; i++)
        {
            var result = await validatorArray[i].ValidateAsync(context, cancellationToken).ConfigureAwait(false);

            if (!result.IsValid)
            {
                for (int j = 0; j < result.Errors.Count; j++)
                {
                    var failure = result.Errors[j];
                    errors.Add(new ValidationError(
                        failure.PropertyName,
                        failure.ErrorCode,
                        failure.ErrorMessage));
                }
            }
        }

        if (errors.Count > 0)
        {
            return CreateValidationFailureResult(errors);
        }

        return await next().ConfigureAwait(false);
    }

    private static TResponse CreateValidationFailureResult(List<ValidationError> validationErrors)
    {
        var errorArray = new Error[validationErrors.Count];
        for (int i = 0; i < validationErrors.Count; i++)
        {
            errorArray[i] = validationErrors[i];
        }

        // If TResponse is Result
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(errorArray);
        }

        // If TResponse is Result<T>
        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = typeof(TResponse).GetMethod(
                "Failure",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(IReadOnlyList<Error>) },
                null);

            if (failureMethod is not null)
            {
                return (TResponse)failureMethod.Invoke(null, new object[] { (IReadOnlyList<Error>)errorArray })!;
            }
        }

        // Fallback: throw validation exception for non-Result responses
        throw new global::FluentValidation.ValidationException(
            validationErrors.Select(e => new global::FluentValidation.Results.ValidationFailure(
                ((ValidationError)e).PropertyName, e.Description) { ErrorCode = e.Code }));
    }
}
