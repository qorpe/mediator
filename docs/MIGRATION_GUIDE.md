# Migration Guide: MediatR to Qorpe.Mediator

## Step 1: Replace Packages

```bash
# Remove MediatR
dotnet remove package MediatR

# Add Qorpe.Mediator
dotnet add package Qorpe.Mediator
dotnet add package Qorpe.Mediator.Behaviors        # Optional: 9 built-in behaviors
dotnet add package Qorpe.Mediator.FluentValidation  # Optional: FluentValidation integration
dotnet add package Qorpe.Mediator.AspNetCore         # Optional: HTTP endpoint mapping
```

## Step 2: Change Registration

```csharp
// Before (MediatR)
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// After (Qorpe.Mediator)
services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
```

## Step 3: Update Namespaces

```csharp
// Before
using MediatR;

// After
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Results;
```

## Step 4: Gradual Type Migration (Optional)

Your existing `IRequest<T>` will still work. When ready, migrate to explicit CQRS types:

```csharp
// Before
public record CreateOrder(string Name) : IRequest<OrderDto>;

// After (explicit CQRS)
public record CreateOrder(string Name) : ICommand<Result<OrderDto>>;
```

## Step 5: Update Handlers

```csharp
// Before (MediatR)
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderDto>
{
    public Task<OrderDto> Handle(CreateOrder request, CancellationToken ct)
    {
        // ...
        return Task.FromResult(dto);
    }
}

// After (Qorpe.Mediator) — ValueTask + Result pattern
public class CreateOrderHandler : ICommandHandler<CreateOrder, Result<OrderDto>>
{
    public ValueTask<Result<OrderDto>> Handle(CreateOrder request, CancellationToken ct)
    {
        // ...
        return new ValueTask<Result<OrderDto>>(Result<OrderDto>.Success(dto));
    }
}
```

## Step 6: Update Pipeline Behaviors

```csharp
// Before (MediatR)
public class LoggingBehavior<TReq, TResp> : IPipelineBehavior<TReq, TResp>
{
    public async Task<TResp> Handle(TReq req, RequestHandlerDelegate<TResp> next, CancellationToken ct)
    {
        // ...
        return await next();
    }
}

// After (Qorpe.Mediator) — ValueTask, same pattern
public class LoggingBehavior<TReq, TResp> : IPipelineBehavior<TReq, TResp>
    where TReq : IRequest<TResp>
{
    public async ValueTask<TResp> Handle(TReq req, RequestHandlerDelegate<TResp> next, CancellationToken ct)
    {
        // ...
        return await next();
    }
}
```

Or just use the 9 built-in behaviors instead of writing your own.

## Step 7: Add Behaviors (Optional)

```csharp
services.AddQorpeValidation(typeof(Program).Assembly);
services.AddQorpeAllBehaviors();
```

## Step 8: Add HTTP Endpoints (Optional)

```csharp
// Add attribute to commands/queries
[HttpEndpoint("POST", "/api/orders")]
public record CreateOrder : ICommand<Result<Guid>> { ... }

// In Program.cs
app.MapQorpeEndpoints(typeof(Program).Assembly);
```

## API Comparison

| MediatR | Qorpe.Mediator |
|---------|----------------|
| `IRequest<T>` | `IRequest<T>`, `ICommand<T>`, `IQuery<T>` |
| `IRequestHandler<TReq, TResp>` | `IRequestHandler<TReq, TResp>`, `ICommandHandler<T>`, `IQueryHandler<T, R>` |
| `INotification` | `INotification`, `IDomainEvent` |
| `INotificationHandler<T>` | `INotificationHandler<T>` |
| `IStreamRequest<T>` | `IStreamRequest<T>` |
| `IPipelineBehavior<T, R>` | `IPipelineBehavior<T, R>` |
| `Task<T>` | `ValueTask<T>` |
| `AddMediatR(...)` | `AddQorpeMediator(...)` |
| N/A | `Result<T>`, `Error`, `ErrorType` |
| N/A | `[HttpEndpoint]`, `[Transactional]`, `[Cacheable]`, etc. |
