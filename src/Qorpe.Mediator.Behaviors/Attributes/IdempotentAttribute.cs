namespace Qorpe.Mediator.Behaviors.Attributes;

/// <summary>
/// Marks a command for idempotency checking. Queries are automatically skipped.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class IdempotentAttribute : Attribute
{
    /// <summary>
    /// Gets the idempotency window in seconds. 0 means no check.
    /// </summary>
    public int WindowSeconds { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="IdempotentAttribute"/>.
    /// </summary>
    /// <param name="windowSeconds">The idempotency window in seconds.</param>
    public IdempotentAttribute(int windowSeconds = 300)
    {
        WindowSeconds = windowSeconds;
    }
}
