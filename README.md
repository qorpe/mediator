# Qorpe.Mediator

**Enterprise-Grade CQRS Mediator for .NET**

[![NuGet](https://img.shields.io/nuget/v/Qorpe.Mediator.svg)](https://www.nuget.org/packages/Qorpe.Mediator/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-blue)](https://dotnet.microsoft.com/)

A production-ready, zero-dependency CQRS mediator library for .NET with Result pattern, 9 built-in pipeline behaviors, DDD support, and attribute-based HTTP endpoint mapping. **MIT licensed. Free forever. No telemetry.**

---

## Why Qorpe.Mediator?

MediatR went commercial in 2025. The .NET community needs a free, better alternative. Qorpe.Mediator is built from scratch — not a fork — fixing known shortcomings and adding enterprise features the community has been asking for.

- **Result Pattern built-in** — No more throwing exceptions for control flow
- **Explicit CQRS** — `ICommand<T>`, `IQuery<T>` instead of just `IRequest`
- **9 built-in behaviors** — Audit, logging, validation, auth, transactions, retry, caching, performance, idempotency
- **Attribute-based HTTP endpoints** — `[HttpEndpoint]` eliminates controller boilerplate
- **DDD native** — `IDomainEvent`, aggregate root patterns
- **Performance first** — Compiled delegate caching, zero per-request allocations on hot paths

---

## Quick Start

### 1. Install

```bash
dotnet add package Qorpe.Mediator
```

### 2. Register

```csharp
builder.Services.AddQorpeMediator(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
```

### 3. Define a Command

```csharp
public record CreateOrderCommand(string UserId, List<OrderItem> Items)
    : ICommand<Result<Guid>>;
```

### 4. Handle it

```csharp
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var orderId = Guid.NewGuid();
        // ... create order
        return new ValueTask<Result<Guid>>(Result<Guid>.Success(orderId));
    }
}
```

### 5. Send

```csharp
var result = await mediator.Send(new CreateOrderCommand("user-1", items));
result.Match(
    id => Console.WriteLine($"Order created: {id}"),
    error => Console.WriteLine($"Failed: {error}")
);
```

---

## Features

### CQRS Separation

```csharp
// Commands — change state
public record CreateOrder(string UserId) : ICommand<Result<Guid>>;
public record CancelOrder(Guid Id) : ICommand<Result>;

// Queries — read state, no side effects
public record GetOrderById(Guid Id) : IQuery<Result<Order>>;

// Streaming
public record SearchOrders(string? Status) : IStreamRequest<Order>;
```

### Result Pattern

```csharp
// Success
return Result<Guid>.Success(orderId);

// Failure with typed errors
return Error.NotFound("Order.NotFound", "Order not found");

// Functional operations
var name = result.Map(order => order.Name)
                 .Bind(name => ValidateName(name))
                 .Match(
                     name => $"Hello, {name}",
                     error => $"Error: {error.Description}");
```

### Domain Events

```csharp
public record OrderCreatedEvent(Guid OrderId, string UserId) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}

// Multiple handlers per event
public class SendEmail : INotificationHandler<OrderCreatedEvent> { ... }
public class UpdateInventory : INotificationHandler<OrderCreatedEvent> { ... }

await publisher.Publish(new OrderCreatedEvent(orderId, userId));
```

### HTTP Endpoint Mapping

```csharp
[HttpEndpoint("POST", "/api/orders", Tags = new[] { "Orders" })]
[Transactional]
[Auditable]
public record CreateOrderCommand : ICommand<Result<Guid>> { ... }

// In Program.cs
app.MapQorpeEndpoints(typeof(Program).Assembly);
// Result auto-mapped: Success->200/201, Validation->400, NotFound->404, etc.
```

---

## Pipeline Behaviors

All behaviors are attribute-driven, configurable, and can be enabled/disabled globally or per-request.

| # | Behavior | Attribute | Description |
|---|----------|-----------|-------------|
| 1 | **Audit** | `[Auditable]` | Logs everything first, async batching, sensitive data masking |
| 2 | **Logging** | Auto | Structured request/response logging with auto-masking |
| 3 | **UnhandledException** | Auto | Catch-all safety net, always re-throws after logging |
| 4 | **Authorization** | `[Authorize]` | Role + policy checking, Result-based responses |
| 5 | **Validation** | Auto | FluentValidation multi-validator, Result.Failure return |
| 6 | **Idempotency** | `[Idempotent]` | SHA256 key, concurrent-safe, window-based expiry |
| 7 | **Transaction** | `[Transactional]` | Command-only, rollback on failure, IUnitOfWork |
| 8 | **Performance** | Auto | Stopwatch-based, configurable warning/critical thresholds |
| 9 | **Retry** | `[Retryable]` | Exponential backoff with jitter, exception type filtering |
| 10 | **Caching** | `[Cacheable]` | Query-only, stampede prevention via per-key semaphore |

### Full Configuration

```csharp
builder.Services.AddQorpeMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.NotificationPublishStrategy = NotificationPublishStrategy.Parallel;
});

builder.Services.AddQorpeValidation(typeof(Program).Assembly);

builder.Services.AddQorpeAllBehaviors(opts =>
{
    opts.ConfigureAudit = audit =>
    {
        audit.AuditCommands = true;
        audit.AuditQueries = false;
        audit.FallbackToConsole = true;
    };
    opts.ConfigureLogging = log =>
    {
        log.MaskProperties.Add("CardNumber");
        log.MaxSerializedLength = 4096;
    };
    opts.ConfigurePerformance = perf =>
    {
        perf.WarningThresholdMs = 500;
        perf.CriticalThresholdMs = 5000;
    };
});
```

---

## MediatR vs Qorpe.Mediator

| Feature | MediatR v12 | Qorpe.Mediator |
|---------|-------------|----------------|
| License | Commercial (2025+) | MIT, free forever |
| Result Pattern | No (exceptions) | Built-in Result\<T\> |
| CQRS Types | IRequest only | ICommand, IQuery, IRequest |
| Domain Events | INotification | IDomainEvent + INotification |
| Streaming | IStreamRequest | IStreamRequest |
| Built-in Behaviors | 0 | 9 (audit, logging, validation, auth, tx, retry, cache, perf, idempotency) |
| HTTP Endpoints | No | [HttpEndpoint] attribute |
| ValueTask | No (Task) | Yes (ValueTask) |
| Pipeline Caching | Per-request rebuild | Cached per request type |
| Handler Resolution | MakeGenericType per call | Compiled delegate caching |
| Validation | Exception-based | Result.Failure (no exceptions) |
| Sensitive Data | No | [SensitiveData] auto-mask |
| Idempotency | No | Built-in [Idempotent] |
| Telemetry | Yes | None |

---

## Packages

| Package | Description |
|---------|-------------|
| `Qorpe.Mediator` | Core — zero dependencies, CQRS abstractions, Result pattern, Mediator implementation |
| `Qorpe.Mediator.FluentValidation` | FluentValidation integration — auto-discovery, multi-validator |
| `Qorpe.Mediator.Behaviors` | 9 built-in pipeline behaviors |
| `Qorpe.Mediator.AspNetCore` | HTTP endpoint mapping — [HttpEndpoint], Result-to-HTTP, OpenAPI |
| `Qorpe.Mediator.Contracts` | Shared contracts for multi-project solutions |

---

## Sample Project

See `tests/Qorpe.Mediator.Sample.ECommerce/` for a complete e-commerce example with:
- Order aggregate root with domain events
- Commands with transactions, audit, authorization, idempotency, and retry
- Queries with caching and streaming
- FluentValidation validators
- Attribute-based HTTP endpoint mapping
- Full behavior pipeline configuration

---

## Migration from MediatR

See [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md) for a step-by-step migration guide.

---

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

---

## License

MIT License. Free forever. No license key. No telemetry.
