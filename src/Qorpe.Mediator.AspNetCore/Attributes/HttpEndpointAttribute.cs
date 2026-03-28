namespace Qorpe.Mediator.AspNetCore.Attributes;

/// <summary>
/// Maps a command or query to an HTTP endpoint via Minimal APIs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class HttpEndpointAttribute : Attribute
{
    /// <summary>
    /// Gets the HTTP method (GET, POST, PUT, DELETE, PATCH).
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// Gets the route pattern (e.g., "/api/orders/{id}").
    /// </summary>
    public string Route { get; }

    /// <summary>
    /// Gets or sets the endpoint group name for route grouping.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Gets or sets OpenAPI tags for documentation.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Gets or sets the OpenAPI summary.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets the OpenAPI description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the success status code. Default is 200 for queries, 201 for commands.
    /// </summary>
    public int SuccessStatusCode { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="HttpEndpointAttribute"/>.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="route">The route pattern.</param>
    public HttpEndpointAttribute(string method, string route)
    {
        Method = method?.ToUpperInvariant() ?? throw new ArgumentNullException(nameof(method));
        Route = route ?? throw new ArgumentNullException(nameof(route));
    }
}
