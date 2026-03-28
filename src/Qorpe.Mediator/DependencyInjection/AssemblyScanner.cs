using System.Reflection;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Exceptions;

namespace Qorpe.Mediator.DependencyInjection;

/// <summary>
/// Scans assemblies for mediator-related types (handlers, behaviors, etc.).
/// </summary>
internal static class AssemblyScanner
{
    private static readonly Type[] HandlerInterfaces =
    {
        typeof(IRequestHandler<,>),
        typeof(INotificationHandler<>),
        typeof(IStreamRequestHandler<,>),
        typeof(IPipelineBehavior<,>),
        typeof(IStreamPipelineBehavior<,>),
        typeof(IRequestPreProcessor<>),
        typeof(IRequestPostProcessor<,>)
    };

    // Interfaces that must have exactly one handler per request type
    private static readonly HashSet<Type> SingleHandlerInterfaces = new()
    {
        typeof(IRequestHandler<,>),
        typeof(IStreamRequestHandler<,>)
    };

    /// <summary>
    /// Scans the given assemblies and returns all discovered handler registrations.
    /// </summary>
    internal static IReadOnlyList<HandlerRegistration> Scan(IReadOnlyList<Assembly> assemblies)
    {
        var registrations = new List<HandlerRegistration>();

        for (int i = 0; i < assemblies.Count; i++)
        {
            var assembly = assemblies[i];
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            for (int j = 0; j < types.Length; j++)
            {
                var type = types[j];
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                var interfaces = type.GetInterfaces();
                for (int k = 0; k < interfaces.Length; k++)
                {
                    var iface = interfaces[k];
                    if (!iface.IsGenericType)
                    {
                        continue;
                    }

                    var genericDef = iface.GetGenericTypeDefinition();
                    for (int l = 0; l < HandlerInterfaces.Length; l++)
                    {
                        if (genericDef == HandlerInterfaces[l])
                        {
                            registrations.Add(new HandlerRegistration(iface, type));
                            break;
                        }
                    }
                }
            }
        }

        ValidateNoDuplicateHandlers(registrations);

        return registrations;
    }
    private static void ValidateNoDuplicateHandlers(List<HandlerRegistration> registrations)
    {
        var singleHandlerMap = new Dictionary<Type, (Type ServiceType, Type ImplementationType)>();

        for (int i = 0; i < registrations.Count; i++)
        {
            var reg = registrations[i];
            if (!reg.ServiceType.IsGenericType)
            {
                continue;
            }

            var genericDef = reg.ServiceType.GetGenericTypeDefinition();
            if (!SingleHandlerInterfaces.Contains(genericDef))
            {
                continue;
            }

            if (singleHandlerMap.TryGetValue(reg.ServiceType, out var existing))
            {
                throw new MultipleHandlersException(reg.ServiceType, 2)
                {
                    Data =
                    {
                        ["Handler1"] = existing.ImplementationType.FullName,
                        ["Handler2"] = reg.ImplementationType.FullName
                    }
                };
            }

            singleHandlerMap[reg.ServiceType] = (reg.ServiceType, reg.ImplementationType);
        }
    }
}

/// <summary>
/// Represents a handler registration discovered by assembly scanning.
/// </summary>
internal readonly struct HandlerRegistration
{
    /// <summary>
    /// The service (interface) type.
    /// </summary>
    public Type ServiceType { get; }

    /// <summary>
    /// The implementation (class) type.
    /// </summary>
    public Type ImplementationType { get; }

    public HandlerRegistration(Type serviceType, Type implementationType)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
    }
}
