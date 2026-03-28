using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using QorpeAbstractions = Qorpe.Mediator.Abstractions;
using QorpeResults = Qorpe.Mediator.Results;
using Qorpe.Mediator.DependencyInjection;

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
public sealed class QorpePassthroughBehavior<TRequest, TResponse> : QorpeAbstractions.IPipelineBehavior<TRequest, TResponse>
    where TRequest : QorpeAbstractions.IRequest<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, QorpeAbstractions.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

// ===== MEDIATR TYPES =====
public sealed class MediatRPingRequest : global::MediatR.IRequest<global::MediatR.Unit> { }
public sealed class MediatRPingRequestHandler : global::MediatR.IRequestHandler<MediatRPingRequest, global::MediatR.Unit>
{
    public Task<global::MediatR.Unit> Handle(MediatRPingRequest request, CancellationToken cancellationToken)
        => Task.FromResult(global::MediatR.Unit.Value);
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
public sealed class MediatRPassthroughBehavior<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : global::MediatR.IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

// ===== COMPREHENSIVE BENCHMARK =====
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MediatorBenchmarks
{
    private QorpeAbstractions.IMediator _qorpe0 = null!;
    private QorpeAbstractions.IMediator _qorpe1B = null!;
    private QorpeAbstractions.IMediator _qorpe3B = null!;
    private QorpeAbstractions.IMediator _qorpe5B = null!;
    private QorpeAbstractions.IMediator _qorpeN1 = null!;
    private QorpeAbstractions.IMediator _qorpeN10 = null!;
    private QorpeAbstractions.IMediator _qorpeN50 = null!;
    private QorpeAbstractions.IMediator _qorpeN100 = null!;
    private global::MediatR.IMediator _mediatR0 = null!;
    private global::MediatR.IMediator _mediatR1B = null!;
    private global::MediatR.IMediator _mediatR3B = null!;
    private global::MediatR.IMediator _mediatR5B = null!;
    private global::MediatR.IMediator _mediatRN1 = null!;
    private global::MediatR.IMediator _mediatRN10 = null!;
    private global::MediatR.IMediator _mediatRN50 = null!;
    private global::MediatR.IMediator _mediatRN100 = null!;

    private readonly QorpePingCommand _qCmd = new();
    private readonly QorpeQueryCommand _qQuery = new();
    private readonly QorpePingNotification _qNotif = new();
    private readonly MediatRPingRequest _mCmd = new();
    private readonly MediatRQueryRequest _mQuery = new();
    private readonly MediatRPingNotification _mNotif = new();

    [GlobalSetup]
    public void Setup()
    {
        var asm = typeof(MediatorBenchmarks).Assembly;

        _qorpe0 = BuildQorpe(asm, 0, 0);
        _qorpe1B = BuildQorpe(asm, 1, 0);
        _qorpe3B = BuildQorpe(asm, 3, 0);
        _qorpe5B = BuildQorpe(asm, 5, 0);
        _qorpeN1 = BuildQorpe(asm, 0, 1);
        _qorpeN10 = BuildQorpe(asm, 0, 10);
        _qorpeN50 = BuildQorpe(asm, 0, 50);
        _qorpeN100 = BuildQorpe(asm, 0, 100);

        _mediatR0 = BuildMediatR(asm, 0, 0);
        _mediatR1B = BuildMediatR(asm, 1, 0);
        _mediatR3B = BuildMediatR(asm, 3, 0);
        _mediatR5B = BuildMediatR(asm, 5, 0);
        _mediatRN1 = BuildMediatR(asm, 0, 1);
        _mediatRN10 = BuildMediatR(asm, 0, 10);
        _mediatRN50 = BuildMediatR(asm, 0, 50);
        _mediatRN100 = BuildMediatR(asm, 0, 100);

        // Warmup all
        _qorpe0.Send(_qCmd).AsTask().Wait();
        _qorpe1B.Send(_qCmd).AsTask().Wait();
        _qorpe3B.Send(_qCmd).AsTask().Wait();
        _qorpe5B.Send(_qCmd).AsTask().Wait();
        _qorpeN1.Publish(_qNotif).AsTask().Wait();
        _qorpeN10.Publish(_qNotif).AsTask().Wait();
        _qorpeN50.Publish(_qNotif).AsTask().Wait();
        _qorpeN100.Publish(_qNotif).AsTask().Wait();
        _mediatR0.Send(_mCmd).Wait();
        _mediatR1B.Send(_mCmd).Wait();
        _mediatR3B.Send(_mCmd).Wait();
        _mediatR5B.Send(_mCmd).Wait();
        _mediatRN1.Publish(_mNotif).Wait();
        _mediatRN10.Publish(_mNotif).Wait();
        _mediatRN50.Publish(_mNotif).Wait();
        _mediatRN100.Publish(_mNotif).Wait();
    }

    private static QorpeAbstractions.IMediator BuildQorpe(System.Reflection.Assembly asm, int behaviors, int notifHandlers)
    {
        var s = new ServiceCollection();
        s.AddQorpeMediator(o => o.RegisterServicesFromAssembly(asm));
        for (int i = 0; i < behaviors; i++)
            s.AddTransient(typeof(QorpeAbstractions.IPipelineBehavior<,>), typeof(QorpePassthroughBehavior<,>));
        for (int i = 1; i < notifHandlers; i++) // 1 already from assembly scan
            s.AddTransient<QorpeAbstractions.INotificationHandler<QorpePingNotification>, QorpePingNotificationHandler>();
        return s.BuildServiceProvider().GetRequiredService<QorpeAbstractions.IMediator>();
    }

    private static global::MediatR.IMediator BuildMediatR(System.Reflection.Assembly asm, int behaviors, int notifHandlers)
    {
        var s = new ServiceCollection();
        s.AddMediatR(c => c.RegisterServicesFromAssembly(asm));
        for (int i = 0; i < behaviors; i++)
            s.AddTransient(typeof(global::MediatR.IPipelineBehavior<,>), typeof(MediatRPassthroughBehavior<,>));
        for (int i = 1; i < notifHandlers; i++)
            s.AddTransient<global::MediatR.INotificationHandler<MediatRPingNotification>, MediatRPingNotificationHandler>();
        return s.BuildServiceProvider().GetRequiredService<global::MediatR.IMediator>();
    }

    // ===== SEND =====
    [Benchmark(Description = "Qorpe Send (0 behaviors)")]
    public ValueTask<QorpeResults.Result> Q_Send_0() => _qorpe0.Send(_qCmd);
    [Benchmark(Description = "MediatR Send (0 behaviors)")]
    public Task<global::MediatR.Unit> M_Send_0() => _mediatR0.Send(_mCmd);

    [Benchmark(Description = "Qorpe Send (1 behavior)")]
    public ValueTask<QorpeResults.Result> Q_Send_1() => _qorpe1B.Send(_qCmd);
    [Benchmark(Description = "MediatR Send (1 behavior)")]
    public Task<global::MediatR.Unit> M_Send_1() => _mediatR1B.Send(_mCmd);

    [Benchmark(Description = "Qorpe Send (3 behaviors)")]
    public ValueTask<QorpeResults.Result> Q_Send_3() => _qorpe3B.Send(_qCmd);
    [Benchmark(Description = "MediatR Send (3 behaviors)")]
    public Task<global::MediatR.Unit> M_Send_3() => _mediatR3B.Send(_mCmd);

    [Benchmark(Description = "Qorpe Send (5 behaviors)")]
    public ValueTask<QorpeResults.Result> Q_Send_5() => _qorpe5B.Send(_qCmd);
    [Benchmark(Description = "MediatR Send (5 behaviors)")]
    public Task<global::MediatR.Unit> M_Send_5() => _mediatR5B.Send(_mCmd);

    // ===== QUERY =====
    [Benchmark(Description = "Qorpe Query (Result<int>)")]
    public ValueTask<QorpeResults.Result<int>> Q_Query() => _qorpe0.Send(_qQuery);
    [Benchmark(Description = "MediatR Query (int)")]
    public Task<int> M_Query() => _mediatR0.Send(_mQuery);

    // ===== PUBLISH =====
    [Benchmark(Description = "Qorpe Publish (1 handler)")]
    public ValueTask Q_Pub_1() => _qorpeN1.Publish(_qNotif);
    [Benchmark(Description = "MediatR Publish (1 handler)")]
    public Task M_Pub_1() => _mediatRN1.Publish(_mNotif);

    [Benchmark(Description = "Qorpe Publish (10 handlers)")]
    public ValueTask Q_Pub_10() => _qorpeN10.Publish(_qNotif);
    [Benchmark(Description = "MediatR Publish (10 handlers)")]
    public Task M_Pub_10() => _mediatRN10.Publish(_mNotif);

    [Benchmark(Description = "Qorpe Publish (50 handlers)")]
    public ValueTask Q_Pub_50() => _qorpeN50.Publish(_qNotif);
    [Benchmark(Description = "MediatR Publish (50 handlers)")]
    public Task M_Pub_50() => _mediatRN50.Publish(_mNotif);

    [Benchmark(Description = "Qorpe Publish (100 handlers)")]
    public ValueTask Q_Pub_100() => _qorpeN100.Publish(_qNotif);
    [Benchmark(Description = "MediatR Publish (100 handlers)")]
    public Task M_Pub_100() => _mediatRN100.Publish(_mNotif);
}
