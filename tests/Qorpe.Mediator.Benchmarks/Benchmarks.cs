using BenchmarkDotNet.Attributes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Behaviors.DependencyInjection;
using Qorpe.Mediator.DependencyInjection;
using QorpeAbstractions = Qorpe.Mediator.Abstractions;
using QorpeResults = Qorpe.Mediator.Results;

namespace Qorpe.Mediator.Benchmarks;

// ===== QORPE TYPES =====

public sealed class QorpePingCommand : QorpeAbstractions.ICommand<QorpeResults.Result> { }
public sealed class QorpePingCommandHandler : QorpeAbstractions.ICommandHandler<QorpePingCommand>
{
    public ValueTask<QorpeResults.Result> Handle(QorpePingCommand request, CancellationToken cancellationToken)
        => ValueTask.FromResult(QorpeResults.Result.Success());
}

public sealed class QorpeQueryCommand : QorpeAbstractions.IQuery<QorpeResults.Result<int>> { }
public sealed class QorpeQueryHandler : QorpeAbstractions.IQueryHandler<QorpeQueryCommand, QorpeResults.Result<int>>
{
    public ValueTask<QorpeResults.Result<int>> Handle(QorpeQueryCommand request, CancellationToken cancellationToken)
        => ValueTask.FromResult(QorpeResults.Result<int>.Success(42));
}

public sealed class QorpePingNotification : QorpeAbstractions.INotification { }
public sealed class QorpePingNotificationHandler : QorpeAbstractions.INotificationHandler<QorpePingNotification>
{
    public ValueTask Handle(QorpePingNotification notification, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}

// A simple behavior for pipeline benchmarks
public sealed class QorpePassthroughBehavior<TRequest, TResponse> : QorpeAbstractions.IPipelineBehavior<TRequest, TResponse>
    where TRequest : QorpeAbstractions.IRequest<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, QorpeAbstractions.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

// ===== MEDIATR TYPES =====

public sealed class MediatRPingRequest : global::MediatR.IRequest<Unit> { }
public sealed class MediatRPingRequestHandler : global::MediatR.IRequestHandler<MediatRPingRequest, Unit>
{
    public Task<Unit> Handle(MediatRPingRequest request, CancellationToken cancellationToken)
        => Task.FromResult(Unit.Value);
}

public sealed class MediatRQueryRequest : global::MediatR.IRequest<int> { }
public sealed class MediatRQueryRequestHandler : global::MediatR.IRequestHandler<MediatRQueryRequest, int>
{
    public Task<int> Handle(MediatRQueryRequest request, CancellationToken cancellationToken)
        => Task.FromResult(42);
}

public sealed class MediatRPingNotification : global::MediatR.INotification { }
public sealed class MediatRPingNotificationHandler : global::MediatR.INotificationHandler<MediatRPingNotification>
{
    public Task Handle(MediatRPingNotification notification, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// MediatR behavior
public sealed class MediatRPassthroughBehavior<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : global::MediatR.IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

// ===== BENCHMARKS =====

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MediatorBenchmarks
{
    // Mediator instances for different scenarios
    private QorpeAbstractions.IMediator _qorpe = null!;
    private QorpeAbstractions.IMediator _qorpe1Behavior = null!;
    private QorpeAbstractions.IMediator _qorpe3Behaviors = null!;
    private QorpeAbstractions.IMediator _qorpe10Handlers = null!;
    private global::MediatR.IMediator _mediatr = null!;
    private global::MediatR.IMediator _mediatr1Behavior = null!;
    private global::MediatR.IMediator _mediatr3Behaviors = null!;
    private global::MediatR.IMediator _mediatr10Handlers = null!;

    // Pre-allocated instances (zero allocation at call site)
    private readonly QorpePingCommand _qorpeCmd = new();
    private readonly QorpeQueryCommand _qorpeQuery = new();
    private readonly QorpePingNotification _qorpeNotif = new();
    private readonly MediatRPingRequest _mediatrCmd = new();
    private readonly MediatRQueryRequest _mediatrQuery = new();
    private readonly MediatRPingNotification _mediatrNotif = new();

    [GlobalSetup]
    public void Setup()
    {
        var asm = typeof(MediatorBenchmarks).Assembly;

        // === Qorpe: No behaviors ===
        var q0 = new ServiceCollection();
        q0.AddQorpeMediator(o => o.RegisterServicesFromAssembly(asm));
        _qorpe = q0.BuildServiceProvider().GetRequiredService<QorpeAbstractions.IMediator>();

        // === Qorpe: 1 behavior ===
        var q1 = new ServiceCollection();
        q1.AddLogging(b => b.ClearProviders());
        q1.AddQorpeMediator(o => o.RegisterServicesFromAssembly(asm));
        q1.AddTransient(typeof(QorpeAbstractions.IPipelineBehavior<,>), typeof(QorpePassthroughBehavior<,>));
        _qorpe1Behavior = q1.BuildServiceProvider().GetRequiredService<QorpeAbstractions.IMediator>();

        // === Qorpe: 3 behaviors ===
        var q3 = new ServiceCollection();
        q3.AddLogging(b => b.ClearProviders());
        q3.AddQorpeMediator(o => o.RegisterServicesFromAssembly(asm));
        for (int i = 0; i < 3; i++)
            q3.AddTransient(typeof(QorpeAbstractions.IPipelineBehavior<,>), typeof(QorpePassthroughBehavior<,>));
        _qorpe3Behaviors = q3.BuildServiceProvider().GetRequiredService<QorpeAbstractions.IMediator>();

        // === Qorpe: 10 notification handlers ===
        var q10 = new ServiceCollection();
        q10.AddQorpeMediator(o => o.RegisterServicesFromAssembly(asm));
        for (int i = 0; i < 9; i++)
            q10.AddTransient<QorpeAbstractions.INotificationHandler<QorpePingNotification>, QorpePingNotificationHandler>();
        _qorpe10Handlers = q10.BuildServiceProvider().GetRequiredService<QorpeAbstractions.IMediator>();

        // === MediatR: No behaviors ===
        var m0 = new ServiceCollection();
        m0.AddMediatR(c => c.RegisterServicesFromAssembly(asm));
        _mediatr = m0.BuildServiceProvider().GetRequiredService<global::MediatR.IMediator>();

        // === MediatR: 1 behavior ===
        var m1 = new ServiceCollection();
        m1.AddMediatR(c => c.RegisterServicesFromAssembly(asm));
        m1.AddTransient(typeof(global::MediatR.IPipelineBehavior<,>), typeof(MediatRPassthroughBehavior<,>));
        _mediatr1Behavior = m1.BuildServiceProvider().GetRequiredService<global::MediatR.IMediator>();

        // === MediatR: 3 behaviors ===
        var m3 = new ServiceCollection();
        m3.AddMediatR(c => c.RegisterServicesFromAssembly(asm));
        for (int i = 0; i < 3; i++)
            m3.AddTransient(typeof(global::MediatR.IPipelineBehavior<,>), typeof(MediatRPassthroughBehavior<,>));
        _mediatr3Behaviors = m3.BuildServiceProvider().GetRequiredService<global::MediatR.IMediator>();

        // === MediatR: 10 notification handlers ===
        var m10 = new ServiceCollection();
        m10.AddMediatR(c => c.RegisterServicesFromAssembly(asm));
        for (int i = 0; i < 9; i++)
            m10.AddTransient<global::MediatR.INotificationHandler<MediatRPingNotification>, MediatRPingNotificationHandler>();
        _mediatr10Handlers = m10.BuildServiceProvider().GetRequiredService<global::MediatR.IMediator>();

        // Warmup — ensure first-call caching is done
        _qorpe.Send(_qorpeCmd).AsTask().Wait();
        _qorpe1Behavior.Send(_qorpeCmd).AsTask().Wait();
        _qorpe3Behaviors.Send(_qorpeCmd).AsTask().Wait();
        _qorpe10Handlers.Publish(_qorpeNotif).AsTask().Wait();
        _mediatr.Send(_mediatrCmd).Wait();
        _mediatr1Behavior.Send(_mediatrCmd).Wait();
        _mediatr3Behaviors.Send(_mediatrCmd).Wait();
        _mediatr10Handlers.Publish(_mediatrNotif).Wait();
    }

    // ===== SEND BENCHMARKS =====

    [Benchmark(Description = "Qorpe Send (0 behaviors)")]
    public ValueTask<QorpeResults.Result> Qorpe_Send_0()
        => _qorpe.Send(_qorpeCmd);

    [Benchmark(Description = "MediatR Send (0 behaviors)")]
    public Task<Unit> MediatR_Send_0()
        => _mediatr.Send(_mediatrCmd);

    [Benchmark(Description = "Qorpe Send (1 behavior)")]
    public ValueTask<QorpeResults.Result> Qorpe_Send_1()
        => _qorpe1Behavior.Send(_qorpeCmd);

    [Benchmark(Description = "MediatR Send (1 behavior)")]
    public Task<Unit> MediatR_Send_1()
        => _mediatr1Behavior.Send(_mediatrCmd);

    [Benchmark(Description = "Qorpe Send (3 behaviors)")]
    public ValueTask<QorpeResults.Result> Qorpe_Send_3()
        => _qorpe3Behaviors.Send(_qorpeCmd);

    [Benchmark(Description = "MediatR Send (3 behaviors)")]
    public Task<Unit> MediatR_Send_3()
        => _mediatr3Behaviors.Send(_mediatrCmd);

    // ===== QUERY WITH RETURN VALUE =====

    [Benchmark(Description = "Qorpe Query (returns Result<int>)")]
    public ValueTask<QorpeResults.Result<int>> Qorpe_Query()
        => _qorpe.Send(_qorpeQuery);

    [Benchmark(Description = "MediatR Query (returns int)")]
    public Task<int> MediatR_Query()
        => _mediatr.Send(_mediatrQuery);

    // ===== PUBLISH BENCHMARKS =====

    [Benchmark(Description = "Qorpe Publish (1 handler)")]
    public ValueTask Qorpe_Publish_1()
        => _qorpe.Publish(_qorpeNotif);

    [Benchmark(Description = "MediatR Publish (1 handler)")]
    public Task MediatR_Publish_1()
        => _mediatr.Publish(_mediatrNotif);

    [Benchmark(Description = "Qorpe Publish (10 handlers)")]
    public ValueTask Qorpe_Publish_10()
        => _qorpe10Handlers.Publish(_qorpeNotif);

    [Benchmark(Description = "MediatR Publish (10 handlers)")]
    public Task MediatR_Publish_10()
        => _mediatr10Handlers.Publish(_mediatrNotif);
}
