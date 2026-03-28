# Qorpe.Mediator

**Enterprise-Grade CQRS Mediator for .NET**

[![NuGet](https://img.shields.io/nuget/vpre/Qorpe.Mediator.svg)](https://www.nuget.org/packages/Qorpe.Mediator/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Qorpe.Mediator.svg)](https://www.nuget.org/packages/Qorpe.Mediator/)
[![Build](https://github.com/qorpe/mediator/actions/workflows/ci.yml/badge.svg)](https://github.com/qorpe/mediator/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-blue)](https://dotnet.microsoft.com/)

A production-ready CQRS mediator library for .NET with Result pattern, pipeline behaviors, DDD support, and attribute-based HTTP endpoint mapping.

- - -

## Why Qorpe.Mediator?

- **Result Pattern built-in** — No more throwing exceptions for control flow
- **Explicit CQRS** — `ICommand<T>`, `IQuery<T>` instead of just `IRequest`
- **10 built-in behaviors** — Audit, logging, validation, auth, transactions, retry, caching, performance, idempotency, cache invalidation
- **Attribute-based HTTP endpoints** — `[HttpEndpoint]` eliminates controller boilerplate
- **DDD native** — `IDomainEvent`, aggregate root patterns
- **Publish performance** — Up to 66% faster notification fanout with 4.7x less memory ([benchmarks](docs/BENCHMARKS.md))
- **221 tests** — Unit, integration, load, E2E

- - -

## Performance

Benchmarked against MediatR v12 using BenchmarkDotNet. Full results: [docs/BENCHMARKS.md](docs/BENCHMARKS.md)

### Publish (Notification Fanout) — Qorpe wins

| Handlers | Qorpe | MediatR v12 | Result |
|----------|-------|-------------|--------|
| 1 handler | 27 ns / 88 B | 50 ns / 288 B | **47% faster, 3.3x less memory** |
| 10 handlers | 75 ns / 376 B | 205 ns / 1,656 B | **63% faster, 4.4x less memory** |
| 100 handlers | 578 ns / 3,256 B | 1,722 ns / 15,336 B | **66% faster, 4.7x less memory** |

### Send (Pipeline) — MediatR has lower latency, Qorpe has more features

| Behaviors | Qorpe | MediatR v12 | Notes |
|-----------|-------|-------------|-------|
| 1 behavior | 83 ns / 352 B | 62 ns / 368 B | Qorpe includes pre/post processors, behavior ordering |
| 5 behaviors | 135 ns / 896 B | 123 ns / 944 B | Qorpe uses less memory |

> Send overhead comes from features MediatR lacks: `IRequestPreProcessor`, `IRequestPostProcessor`, `IBehaviorOrder`, cancellation diagnostics. The ~30 ns difference is negligible vs typical handler execution (1-100+ ms).

- - -

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

- - -

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

- - -

## Pipeline Behaviors

All behaviors are attribute-driven, configurable, and automatically ordered via `IBehaviorOrder`.

| # | Behavior | Attribute | Order | Description |
|---|----------|-----------|-------|-------------|
| 1 | **Audit** | `[Auditable]` | 100 | Async batching, store abstraction, sensitive data masking |
| 2 | **Logging** | Auto | 200 | Structured logging, auto-mask, circular reference safe |
| 3 | **UnhandledException** | Auto | 300 | Catch-all safety net, always re-throws |
| 4 | **Authorization** | `[Authorize]` | 400 | Role + policy checking, Result-based responses |
| 5 | **Validation** | Auto | 500 | FluentValidation multi-validator, Result.Failure |
| 6 | **Idempotency** | `[Idempotent]` | 600 | SHA256 key, per-key locking, window-based expiry |
| 7 | **Transaction** | `[Transactional]` | 700 | Command-only, rollback, distinct commit/handler errors |
| 8 | **Performance** | `[PerformanceThreshold]` | 800 | Per-request thresholds, 30s hard ceiling |
| 9 | **Retry** | `[Retryable]` | 900 | Exponential backoff with jitter, success attempt logging |
| 10 | **Caching** | `[Cacheable]` | 1000 | Query-only, bounded lock pool, stampede prevention |
| 11 | **Cache Invalidation** | `[InvalidatesCache]` | 1001 | Command-driven cache invalidation by key prefix |

### Full Configuration

```csharp
builder.Services.AddQorpeMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.NotificationPublishStrategy = NotificationPublishStrategy.Parallel;
    cfg.EnablePolymorphicNotifications = true;
    cfg.ValidateOnStartup = true;
});

builder.Services.AddQorpeValidation(typeof(Program).Assembly);

builder.Services.AddQorpeAllBehaviors(opts =>
{
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

- - -

## MediatR vs Qorpe.Mediator

| Feature | MediatR v12 | Qorpe.Mediator |
|---------|-------------|----------------|
| License | Commercial (2025+) | **MIT** |
| Publish Performance | Baseline | **Up to 66% faster** |
| Publish Memory | Baseline | **Up to 4.7x less** |
| Result Pattern | No (exceptions) | **Built-in Result\<T\>** |
| CQRS Types | IRequest only | **ICommand, IQuery, IRequest** |
| Domain Events | INotification | **IDomainEvent + INotification** |
| Pre/Post Processors | Defined, wired | **Defined, wired** |
| Behavior Ordering | Registration order | **Explicit IBehaviorOrder** |
| Polymorphic Notifications | No | **Opt-in** |
| Startup Validation | No | **ValidateOnStartup** |
| Built-in Behaviors | 0 | **11** |
| HTTP Endpoints | No | **[HttpEndpoint] attribute** |
| Cache Invalidation | No | **[InvalidatesCache] attribute** |
| ValueTask | No (Task) | **Yes (ValueTask)** |
| Cancellation Diagnostics | No | **Pipeline stage tracking** |
| Stream Pipeline Behaviors | No | **IStreamPipelineBehavior** |
| Sensitive Data | No | **[SensitiveData] auto-mask** |
| Telemetry | Yes | **None** |

- - -

## Test Coverage

| Layer | Tests | What It Covers |
|-------|-------|----------------|
| **Unit** | 182 | Result, Error, Guard, Mediator, all behaviors, notifications, validation, pre/post processors |
| **Integration** | 21 | Full pipeline E2E, HTTP endpoints, cross-behavior, DI registration |
| **Load** | 18 | 50K concurrent, 500K sequential, memory stability, streaming, latency percentiles |
| **Total** | **221** | Production-grade coverage |

- - -

## Packages

| Package | Description |
|---------|-------------|
| `Qorpe.Mediator` | Core — CQRS abstractions, Result pattern, Mediator implementation |
| `Qorpe.Mediator.Behaviors` | 11 built-in pipeline behaviors |
| `Qorpe.Mediator.FluentValidation` | FluentValidation integration — auto-discovery, multi-validator |
| `Qorpe.Mediator.AspNetCore` | HTTP endpoint mapping — [HttpEndpoint], Result-to-HTTP, OpenAPI |
| `Qorpe.Mediator.Contracts` | Shared contracts for multi-project solutions |

- - -

## Sample Project

See [`tests/Qorpe.Mediator.Sample.ECommerce/`](tests/Qorpe.Mediator.Sample.ECommerce/) for a complete e-commerce example with:
- Order aggregate root with domain events
- Commands with transactions, audit, authorization, idempotency, and retry
- Queries with caching and streaming
- FluentValidation validators
- Attribute-based HTTP endpoint mapping
- Full behavior pipeline configuration

- - -

## Migration from MediatR

See [docs/MIGRATION_GUIDE.md](docs/MIGRATION_GUIDE.md) for a step-by-step migration guide.

- - -

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

- - -

## License

[MIT License](LICENSE)
