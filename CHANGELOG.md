# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-03-28

### Added
- **Core Package (Qorpe.Mediator)**
  - CQRS abstractions: ICommand, IQuery, IRequest, INotification, IDomainEvent
  - Handler interfaces: IRequestHandler, ICommandHandler, IQueryHandler, INotificationHandler
  - Pipeline: IPipelineBehavior, IRequestPreProcessor, IRequestPostProcessor
  - Streaming: IStreamRequest, IStreamRequestHandler
  - Result pattern: Result, Result\<T\>, Error, ErrorType, ValidationError
  - Functional extensions: Map, Bind, Match (sync and async)
  - Mediator implementation with compiled delegate caching
  - Notification publishers: Sequential and Parallel with timeout
  - DI registration: AddQorpeMediator with assembly scanning
  - Audit infrastructure: AuditEntry, IAuditStore, InMemoryAuditStore
  - Guard class for argument validation
  - Custom exceptions: HandlerNotFoundException, MultipleHandlersException

- **Behaviors Package (Qorpe.Mediator.Behaviors)**
  - AuditBehavior with async batching and store abstraction
  - LoggingBehavior with structured logging and sensitive data masking
  - UnhandledExceptionBehavior as catch-all safety net
  - AuthorizationBehavior with role and policy checking
  - TransactionBehavior with command-only execution and rollback
  - IdempotencyBehavior with SHA256 key generation
  - PerformanceBehavior with Stopwatch-based monitoring
  - RetryBehavior with exponential backoff and jitter
  - CachingBehavior with stampede prevention
  - Behavior attributes: Auditable, Cacheable, Retryable, Authorize, Idempotent, Transactional

- **FluentValidation Package (Qorpe.Mediator.FluentValidation)**
  - ValidationBehavior with multi-validator support
  - Auto-discovery of validators from assemblies
  - Result.Failure return (no exception throwing)

- **AspNetCore Package (Qorpe.Mediator.AspNetCore)**
  - HttpEndpointAttribute for declarative endpoint mapping
  - EndpointMapper for auto-discovery and Minimal API generation
  - ResultToActionResultMapper with RFC 7807 ProblemDetails
  - Route grouping and OpenAPI metadata support

- **Contracts Package (Qorpe.Mediator.Contracts)**
  - Re-exports core abstractions for multi-project solutions

- **Sample E-Commerce Project**
  - Complete order management with domain events
  - Demonstrates all behaviors and endpoint mapping

- **Unit Tests**
  - 58 tests covering core, Result pattern, guards, notifications, mediator
