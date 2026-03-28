using Microsoft.Extensions.DependencyInjection;

namespace Qorpe.Mediator.AspNetCore.Extensions;

/// <summary>
/// Options for Qorpe.Mediator ASP.NET Core integration.
/// </summary>
public sealed class QorpeEndpointOptions
{
    /// <summary>
    /// Gets or sets whether to use ProblemDetails for error responses. Default is true.
    /// </summary>
    public bool UseProblemDetails { get; set; } = true;
}

/// <summary>
/// Extension methods for registering Qorpe.Mediator ASP.NET Core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Qorpe.Mediator ASP.NET Core endpoint support.
    /// </summary>
    public static IServiceCollection AddQorpeEndpoints(
        this IServiceCollection services,
        Action<QorpeEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new QorpeEndpointOptions();
        configure?.Invoke(options);

        if (options.UseProblemDetails)
        {
            services.AddProblemDetails();
        }

        services.AddEndpointsApiExplorer();

        return services;
    }
}
