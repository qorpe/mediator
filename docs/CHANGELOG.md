# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0-beta.8] - 2026-03-29

### Performance
- **Attribute reflection caching** — All behavior attribute lookups (`[Retryable]`, `[Cacheable]`, `[Authorize]`, etc.) now use `static readonly` fields, eliminating per-request `GetCustomAttributes` reflection (#64)
- **Type check caching** — `IsCommand()`/`IsQuery()` interface checks in CachingBehavior, IdempotencyBehavior, TransactionBehavior cached as `static readonly bool` (#66)
- **Cache key optimization** — CachingBehavior now uses SHA256 hash for cache keys instead of raw JSON strings, preventing excessively long keys (#70)
- **Auth result caching** — AuthorizationBehavior `Result<T>.Failure()` method lookup cached as compiled delegate instead of per-invocation reflection (#72)

### Fixed
- **RetryBehavior TaskCanceledException** — The pattern `TaskCanceledException and not OperationCanceledException` was always unreachable due to inheritance. HttpClient timeout exceptions are now correctly retried using token comparison (#68)
- **Stream behavior ordering** — StreamHandlerWrapper now sorts `IStreamPipelineBehavior` by `IBehaviorOrder.Order`, matching RequestHandlerWrapper behavior (#74)

### Testing
- **Behavior ordering tests** — 4 tests verifying IBehaviorOrder-based execution order (ascending, default position, stable sort) (#76)
- **Stream ordering tests** — 2 tests verifying stream behavior ordering (#78)
- **Total tests: 252** (213 unit + 21 integration + 18 load)

### CI/CD
- **CodeQL security scanning** — Added GitHub CodeQL workflow for C# SAST analysis (#81)

## [1.0.0] - 2025-03-28

### Core Architecture
- **Qorpe.Mediator** — Zero-dependency CQRS mediator core
  - CQRS abstractions: `ICommand<T>`, `IQuery<T>`, `IRequest<T>`, `INotification`, `IDomainEvent`
  - Handler interfaces: `IRequestHandler`, `ICommandHandler`, `IQueryHandler`, `INotificationHandler`
  - Pipeline: `IPipelineBehavior<T,R>`, `IRequestPreProcessor<T>`, `IRequestPostProcessor<T,R>`
  - Streaming: `IStreamRequest<T>`, `IStreamRequestHandler<T,R>`
  - Result pattern: `Result`, `Result<T>`, `Error`, `ErrorType`, `ValidationError`
  - Functional extensions: `Map`, `Bind`, `Match` (sync and async)
  - Guard class for argument validation
  - Custom exceptions: `HandlerNotFoundException`, `MultipleHandlersException`, `PipelineException`

### Performance Engine
- Typed `RequestHandlerWrapper<TRequest, TResponse>` with compiled Expression Tree delegates
- Zero reflection on hot path after first call per request type
- Direct notification handler invocation — no wrapper object allocation
- `ForeachNotificationPublisher` (sequential) and `ParallelNotificationPublisher` (concurrent)
- DI registration via `AddQorpeMediator` with assembly scanning

### Pipeline Behaviors (Qorpe.Mediator.Behaviors)
- **AuditBehavior** — async batching, store abstraction, sensitive data masking, console fallback
- **LoggingBehavior** — structured logging, auto-mask by name + attribute, truncation
- **UnhandledExceptionBehavior** — catch-all safety net
- **AuthorizationBehavior** — role + policy checking, `Result.Unauthorized`/`Result.Forbidden`
- **TransactionBehavior** — command-only, rollback, nested savepoints via `IUnitOfWork`
- **IdempotencyBehavior** — SHA256 key, concurrent-safe, window-based expiry
- **PerformanceBehavior** — Stopwatch-based, configurable warning/critical thresholds
- **RetryBehavior** — exponential backoff with jitter, exception type filtering
- **CachingBehavior** — query-only, stampede prevention via per-key `SemaphoreSlim`
- Attributes: `[Auditable]`, `[Cacheable]`, `[Retryable]`, `[Authorize]`, `[Idempotent]`, `[Transactional]`

### FluentValidation (Qorpe.Mediator.FluentValidation)
- `ValidationBehavior` — multi-validator, runs ALL, returns `Result.Failure` (no exceptions)
- Auto-discovery from assemblies

### ASP.NET Core (Qorpe.Mediator.AspNetCore)
- `[HttpEndpoint]` attribute for declarative Minimal API routing
- `EndpointMapper` — auto-discovers and generates endpoints
- `ResultToActionResultMapper` — RFC 7807 ProblemDetails
- Route grouping, OpenAPI metadata

### Contracts (Qorpe.Mediator.Contracts)
- Re-exports core abstractions for multi-project solutions

### Sample Project
- E-Commerce: Order aggregate, domain events, commands, queries, validators
- Full behavior pipeline, attribute-based HTTP endpoints

### Benchmarks (vs MediatR v12)
- Send with behaviors: **19-28% faster**
- Publish: **38-65% faster**, **3.3-4.7x less memory**
- 18 benchmark scenarios, all honest BenchmarkDotNet results

### Tests (149 total)
- **123 unit tests** — Result, Error, Guard, Mediator, all 9 behaviors, notifications, validation, behavior execution verification
- **9 integration tests** — full pipeline E2E, cross-behavior, DI, audit trail
- **17 load tests** — 50K concurrent, 500K sequential, re-entrancy, streaming, latency percentiles (p50/p95/p99), thread pool, scoped DI, cancellation, graceful degradation
