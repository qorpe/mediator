using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.AspNetCore.Attributes;
using Qorpe.Mediator.Results;

namespace Qorpe.Mediator.AspNetCore.Mapping;

/// <summary>
/// Discovers [HttpEndpoint] attributes and maps them to Minimal API endpoints.
/// </summary>
public static class EndpointMapper
{
    /// <summary>
    /// Maps all discovered HttpEndpoint attributes to Minimal API endpoints.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapQorpeEndpoints(
        this IEndpointRouteBuilder app,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(assemblies);

        var discoveredEndpoints = DiscoverEndpoints(assemblies);
        var routeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in discoveredEndpoints)
        {
            var routeKey = $"{endpoint.Attribute.Method}:{endpoint.Attribute.Route}";
            if (!routeSet.Add(routeKey))
            {
                throw new InvalidOperationException(
                    $"Duplicate route '{endpoint.Attribute.Method} {endpoint.Attribute.Route}' found on type '{endpoint.RequestType.Name}'. " +
                    "Each HTTP method + route combination must be unique.");
            }

            // Validate: HttpEndpoint on INotification is not allowed
            if (typeof(INotification).IsAssignableFrom(endpoint.RequestType))
            {
                throw new InvalidOperationException(
                    $"[HttpEndpoint] cannot be applied to notification type '{endpoint.RequestType.Name}'. " +
                    "Only commands and queries can be mapped to HTTP endpoints.");
            }

            MapEndpoint(app, endpoint);
        }

        return app;
    }

    private static List<EndpointDescriptor> DiscoverEndpoints(Assembly[] assemblies)
    {
        var result = new List<EndpointDescriptor>();

        for (int i = 0; i < assemblies.Length; i++)
        {
            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            for (int j = 0; j < types.Length; j++)
            {
                var type = types[j];
                var attr = type.GetCustomAttribute<HttpEndpointAttribute>();
                if (attr is null) continue;

                result.Add(new EndpointDescriptor(type, attr));
            }
        }

        return result;
    }

    private static void MapEndpoint(IEndpointRouteBuilder app, EndpointDescriptor descriptor)
    {
        var requestType = descriptor.RequestType;
        var attr = descriptor.Attribute;

        // Find the response type from IRequest<TResponse>
        var requestInterface = requestType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

        if (requestInterface is null)
        {
            throw new InvalidOperationException(
                $"Type '{requestType.Name}' has [HttpEndpoint] but does not implement IRequest<TResponse>.");
        }

        var responseType = requestInterface.GetGenericArguments()[0];
        var isCommand = requestType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

        var defaultSuccessCode = attr.SuccessStatusCode > 0
            ? attr.SuccessStatusCode
            : (isCommand && attr.Method == "POST" ? StatusCodes.Status201Created : StatusCodes.Status200OK);

        var routeBuilder = attr.Method switch
        {
            "GET" => app.MapGet(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            "POST" => app.MapPost(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            "PUT" => app.MapPut(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            "DELETE" => app.MapDelete(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            "PATCH" => app.MapPatch(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            _ => throw new InvalidOperationException($"Unsupported HTTP method '{attr.Method}' on type '{requestType.Name}'.")
        };

        // Apply metadata
        if (attr.Tags is { Length: > 0 })
        {
            routeBuilder.WithTags(attr.Tags);
        }

        if (attr.Summary is not null)
        {
            routeBuilder.WithSummary(attr.Summary);
        }

        if (attr.Description is not null)
        {
            routeBuilder.WithDescription(attr.Description);
        }

        routeBuilder.WithName(requestType.Name);
    }

    // Cache: requestType -> compiled send delegate (one MakeGenericMethod per type, not per request)
    private static readonly ConcurrentDictionary<Type, Func<ISender, object, CancellationToken, Task<object?>>> SendDelegateCache = new();

    private static Delegate CreateHandler(Type requestType, Type responseType, int successStatusCode)
    {
        // Build and cache the typed send delegate at registration time (not per-request)
        var sendDelegate = SendDelegateCache.GetOrAdd(requestType, static (_, respType) =>
        {
            var helperMethod = typeof(EndpointMapper)
                .GetMethod(nameof(InvokeSendTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(respType);

            return (Func<ISender, object, CancellationToken, Task<object?>>)
                Delegate.CreateDelegate(typeof(Func<ISender, object, CancellationToken, Task<object?>>), helperMethod);
        }, responseType);

        return async (HttpContext context, ISender sender) =>
        {
            object? request;

            if (context.Request.Method == "GET")
            {
                request = BindFromQueryAndRoute(context, requestType);
            }
            else
            {
                request = await context.Request.ReadFromJsonAsync(requestType, context.RequestAborted);
            }

            if (request is null)
            {
                return Microsoft.AspNetCore.Http.Results.BadRequest(
                    new { error = "Request body cannot be null." });
            }

            BindRouteParameters(context, request, requestType);

            // Cached delegate — zero reflection per request
            var response = await sendDelegate(sender, request, context.RequestAborted);

            return MapResponseToHttpResult(response, successStatusCode);
        };
    }

    private static async Task<object?> InvokeSendTyped<TResponse>(ISender sender, object request, CancellationToken ct)
    {
        var result = await sender.Send((IRequest<TResponse>)request, ct).ConfigureAwait(false);
        return result;
    }

    private static IResult MapResponseToHttpResult(object? response, int successStatusCode)
    {
        if (response is null)
        {
            return Microsoft.AspNetCore.Http.Results.Ok();
        }

        if (response is Result result)
        {
            return ResultToActionResultMapper.ToHttpResult(result, successStatusCode);
        }

        // Check if it's Result<T>
        var responseType = response.GetType();
        if (responseType.IsGenericType &&
            responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var isSuccess = (bool)responseType.GetProperty("IsSuccess")!.GetValue(response)!;
            if (isSuccess)
            {
                var value = responseType.GetProperty("Value")!.GetValue(response);
                return successStatusCode == StatusCodes.Status201Created
                    ? Microsoft.AspNetCore.Http.Results.Created(string.Empty, value)
                    : Microsoft.AspNetCore.Http.Results.Ok(value);
            }

            var errors = (IReadOnlyList<Error>)responseType.GetProperty("Errors")!.GetValue(response)!;
            return ResultToActionResultMapper.ToHttpResult(Result.Failure(errors), successStatusCode);
        }

        return Microsoft.AspNetCore.Http.Results.Ok(response);
    }

    private static object BindFromQueryAndRoute(HttpContext context, Type requestType)
    {
        var instance = Activator.CreateInstance(requestType)!;
        var properties = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        for (int i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            if (!prop.CanWrite) continue;

            // Try route values first, then query string
            string? value = context.Request.RouteValues.TryGetValue(prop.Name, out var routeVal)
                ? routeVal?.ToString()
                : context.Request.Query[prop.Name].FirstOrDefault();

            if (value is not null)
            {
                var converted = ConvertValue(value, prop.PropertyType);
                if (converted is not null)
                {
                    prop.SetValue(instance, converted);
                }
            }
        }

        return instance;
    }

    private static void BindRouteParameters(HttpContext context, object request, Type requestType)
    {
        var properties = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            if (!prop.CanWrite) continue;

            if (context.Request.RouteValues.TryGetValue(prop.Name, out var routeVal) && routeVal is not null)
            {
                var converted = ConvertValue(routeVal.ToString()!, prop.PropertyType);
                if (converted is not null)
                {
                    prop.SetValue(request, converted);
                }
            }
        }
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        try
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // Enum: case-insensitive parsing matching ASP.NET Core conventions
            if (underlyingType.IsEnum)
            {
                return Enum.TryParse(underlyingType, value, ignoreCase: true, out var enumResult)
                    ? enumResult
                    : null;
            }

            // Guid: special handling (Convert.ChangeType doesn't support Guid)
            if (underlyingType == typeof(Guid))
            {
                return Guid.TryParse(value, out var guidResult) ? guidResult : null;
            }

            return Convert.ChangeType(value, underlyingType, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private sealed class EndpointDescriptor
    {
        public Type RequestType { get; }
        public HttpEndpointAttribute Attribute { get; }

        public EndpointDescriptor(Type requestType, HttpEndpointAttribute attribute)
        {
            RequestType = requestType;
            Attribute = attribute;
        }
    }
}
