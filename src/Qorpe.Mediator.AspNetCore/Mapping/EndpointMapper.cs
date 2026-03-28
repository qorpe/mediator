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

    private static Delegate CreateHandler(Type requestType, Type responseType, int successStatusCode)
    {
        return async (HttpContext context, ISender sender) =>
        {
            object? request;

            if (context.Request.Method == "GET")
            {
                // Bind from query string and route values
                request = BindFromQueryAndRoute(context, requestType);
            }
            else
            {
                // Bind from body
                request = await context.Request.ReadFromJsonAsync(requestType, context.RequestAborted);
            }

            if (request is null)
            {
                return Microsoft.AspNetCore.Http.Results.BadRequest(
                    new { error = "Request body cannot be null." });
            }

            // Bind route parameters
            BindRouteParameters(context, request, requestType);

            // Use reflection to call Send<TResponse>
            var sendMethod = typeof(ISender)
                .GetMethod(nameof(ISender.Send))!
                .MakeGenericMethod(responseType);

            var task = sendMethod.Invoke(sender, new[] { request, context.RequestAborted });
            var response = await ((dynamic)task!);

            // Map Result to HTTP result
            if (response is Result result)
            {
                return ResultToActionResultMapper.ToHttpResult(result, successStatusCode);
            }

            // Check if it's Result<T>
            var responseObj = (object)response;
            if (responseObj.GetType().IsGenericType &&
                responseObj.GetType().GetGenericTypeDefinition() == typeof(Result<>))
            {
                var isSuccess = (bool)responseObj.GetType().GetProperty("IsSuccess")!.GetValue(responseObj)!;
                if (isSuccess)
                {
                    var value = responseObj.GetType().GetProperty("Value")!.GetValue(responseObj);
                    return successStatusCode == StatusCodes.Status201Created
                        ? Microsoft.AspNetCore.Http.Results.Created(string.Empty, value)
                        : Microsoft.AspNetCore.Http.Results.Ok(value);
                }

                var error = (Error)responseObj.GetType().GetProperty("Error")!.GetValue(responseObj)!;
                var errors = (IReadOnlyList<Error>)responseObj.GetType().GetProperty("Errors")!.GetValue(responseObj)!;
                return ResultToActionResultMapper.ToHttpResult(Result.Failure(errors), successStatusCode);
            }

            return Microsoft.AspNetCore.Http.Results.Ok(response);
        };
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
                try
                {
                    var converted = Convert.ChangeType(value, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType, System.Globalization.CultureInfo.InvariantCulture);
                    prop.SetValue(instance, converted);
                }
                catch
                {
                    // Skip invalid conversions
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
                try
                {
                    var converted = Convert.ChangeType(routeVal.ToString(),
                        Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType,
                        System.Globalization.CultureInfo.InvariantCulture);
                    prop.SetValue(request, converted);
                }
                catch
                {
                    // Skip
                }
            }
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
