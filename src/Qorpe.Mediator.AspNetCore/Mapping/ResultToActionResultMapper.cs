using Microsoft.AspNetCore.Http;
using Qorpe.Mediator.Results;

namespace Qorpe.Mediator.AspNetCore.Mapping;

/// <summary>
/// Maps Result types to HTTP responses with RFC 7807 ProblemDetails.
/// </summary>
public static class ResultToActionResultMapper
{
    /// <summary>
    /// Maps a Result to an IResult for Minimal API endpoints.
    /// </summary>
    public static IResult ToHttpResult(Result result, int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return successStatusCode == StatusCodes.Status201Created
                ? Microsoft.AspNetCore.Http.Results.Created()
                : Microsoft.AspNetCore.Http.Results.Ok();
        }

        return MapErrorToHttpResult(result.Error, result.Errors);
    }

    /// <summary>
    /// Maps a Result of T to an IResult for Minimal API endpoints.
    /// </summary>
    public static IResult ToHttpResult<T>(Result<T> result, int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return successStatusCode == StatusCodes.Status201Created
                ? Microsoft.AspNetCore.Http.Results.Created(string.Empty, result.Value)
                : Microsoft.AspNetCore.Http.Results.Ok(result.Value);
        }

        return MapErrorToHttpResult(result.Error, result.Errors);
    }

    private static IResult MapErrorToHttpResult(Error error, IReadOnlyList<Error> errors)
    {
        return error.Type switch
        {
            ErrorType.Validation => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error",
                detail: error.Description,
                extensions: CreateValidationExtensions(errors)),

            ErrorType.NotFound => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: error.Description),

            ErrorType.Conflict => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: error.Description),

            ErrorType.Unauthorized => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: error.Description),

            ErrorType.Forbidden => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: error.Description),

            ErrorType.Unavailable => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service Unavailable",
                detail: error.Description),

            _ => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: error.Description)
        };
    }

    private static Dictionary<string, object?> CreateValidationExtensions(IReadOnlyList<Error> errors)
    {
        var validationErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        for (int i = 0; i < errors.Count; i++)
        {
            var error = errors[i];
            if (error is ValidationError validationError)
            {
                if (!validationErrors.TryGetValue(validationError.PropertyName, out var existing))
                {
                    validationErrors[validationError.PropertyName] = new[] { error.Description };
                }
                else
                {
                    var newArray = new string[existing.Length + 1];
                    existing.CopyTo(newArray, 0);
                    newArray[existing.Length] = error.Description;
                    validationErrors[validationError.PropertyName] = newArray;
                }
            }
        }

        return new Dictionary<string, object?>
        {
            ["errors"] = validationErrors
        };
    }
}
