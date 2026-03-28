using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Implementation;

namespace Qorpe.Mediator.DependencyInjection;

/// <summary>
/// Extension methods for registering Qorpe.Mediator services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Qorpe.Mediator services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public static IServiceCollection AddQorpeMediator(
        this IServiceCollection services,
        Action<MediatorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MediatorOptions();
        configure(options);

        return AddQorpeMediatorInternal(services, options);
    }

    /// <summary>
    /// Adds Qorpe.Mediator services to the service collection with default options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">The assemblies to scan for handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQorpeMediator(
        this IServiceCollection services,
        params System.Reflection.Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        var options = new MediatorOptions();
        options.RegisterServicesFromAssemblies(assemblies);

        return AddQorpeMediatorInternal(services, options);
    }

    private static IServiceCollection AddQorpeMediatorInternal(
        IServiceCollection services,
        MediatorOptions options)
    {
        // Register the notification publisher based on strategy
        RegisterNotificationPublisher(services, options);

        // Register the mediator
        services.TryAddTransient<IMediator, Implementation.Mediator>();
        services.TryAddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.TryAddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        // Scan assemblies and register handlers
        if (options.AssembliesToRegister.Count > 0)
        {
            var registrations = AssemblyScanner.Scan(options.AssembliesToRegister);

            for (int i = 0; i < registrations.Count; i++)
            {
                var reg = registrations[i];
                var descriptor = new ServiceDescriptor(reg.ServiceType, reg.ImplementationType, options.HandlerLifetime);
                services.TryAdd(descriptor);
            }
        }

        return services;
    }

    private static void RegisterNotificationPublisher(
        IServiceCollection services,
        MediatorOptions options)
    {
        switch (options.NotificationPublishStrategy)
        {
            case NotificationPublishStrategy.Sequential:
                services.TryAddSingleton<INotificationPublisher>(new ForeachNotificationPublisher(stopOnFirstError: true));
                break;
            case NotificationPublishStrategy.SequentialContinueOnError:
                services.TryAddSingleton<INotificationPublisher>(new ForeachNotificationPublisher(stopOnFirstError: false));
                break;
            case NotificationPublishStrategy.Parallel:
                services.TryAddSingleton<INotificationPublisher>(new ParallelNotificationPublisher());
                break;
            case NotificationPublishStrategy.ParallelWithTimeout:
                services.TryAddSingleton<INotificationPublisher>(new ParallelNotificationPublisher(options.ParallelTimeout));
                break;
            default:
                services.TryAddSingleton<INotificationPublisher>(new ForeachNotificationPublisher(stopOnFirstError: true));
                break;
        }
    }
}
