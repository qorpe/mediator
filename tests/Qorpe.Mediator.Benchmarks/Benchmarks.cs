using BenchmarkDotNet.Attributes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Qorpe.Mediator.DependencyInjection;
using QorpeAbstractions = Qorpe.Mediator.Abstractions;
using QorpeResults = Qorpe.Mediator.Results;

namespace Qorpe.Mediator.Benchmarks;

// ---------------------------------------------------------------------------
// Qorpe.Mediator request / notification types
// ---------------------------------------------------------------------------

public sealed class QorpePingCommand : QorpeAbstractions.ICommand<QorpeResults.Result>
{
}

public sealed class QorpePingCommandHandler
    : QorpeAbstractions.ICommandHandler<QorpePingCommand>
{
    public ValueTask<QorpeResults.Result> Handle(
        QorpePingCommand request,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(QorpeResults.Result.Success());
}

public sealed class QorpePingNotification : QorpeAbstractions.INotification
{
}

public sealed class QorpePingNotificationHandler
    : QorpeAbstractions.INotificationHandler<QorpePingNotification>
{
    public ValueTask Handle(
        QorpePingNotification notification,
        CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}

// ---------------------------------------------------------------------------
// MediatR request / notification types
// ---------------------------------------------------------------------------

public sealed class MediatRPingRequest : IRequest<Unit>
{
}

public sealed class MediatRPingRequestHandler : IRequestHandler<MediatRPingRequest, Unit>
{
    public Task<Unit> Handle(
        MediatRPingRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(Unit.Value);
}

public sealed class MediatRPingNotification : INotification
{
}

public sealed class MediatRPingNotificationHandler : INotificationHandler<MediatRPingNotification>
{
    public Task Handle(
        MediatRPingNotification notification,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Benchmark class
// ---------------------------------------------------------------------------

[MemoryDiagnoser]
public class MediatorBenchmarks
{
    // Qorpe with 1 notification handler
    private QorpeAbstractions.IMediator _qorpeMediator1Handler = null!;
    // Qorpe with 10 notification handlers
    private QorpeAbstractions.IMediator _qorpeMediator10Handlers = null!;

    // MediatR with 1 notification handler
    private global::MediatR.IMediator _mediatR1Handler = null!;
    // MediatR with 10 notification handlers
    private global::MediatR.IMediator _mediatR10Handlers = null!;

    // Pre-allocated request/notification instances
    private readonly QorpePingCommand _qorpePingCommand = new();
    private readonly QorpePingNotification _qorpePingNotification = new();
    private readonly MediatRPingRequest _mediatRPingRequest = new();
    private readonly MediatRPingNotification _mediatRPingNotification = new();

    [GlobalSetup]
    public void Setup()
    {
        var currentAssembly = typeof(MediatorBenchmarks).Assembly;

        // ---- Qorpe – 1 notification handler --------------------------------
        var qorpeServices1 = new ServiceCollection();
        qorpeServices1.AddQorpeMediator(opts =>
            opts.RegisterServicesFromAssembly(currentAssembly));
        _qorpeMediator1Handler = qorpeServices1.BuildServiceProvider()
            .GetRequiredService<QorpeAbstractions.IMediator>();

        // ---- Qorpe – 10 notification handlers ------------------------------
        var qorpeServices10 = new ServiceCollection();
        qorpeServices10.AddQorpeMediator(opts =>
            opts.RegisterServicesFromAssembly(currentAssembly));
        // Register 9 additional handlers (1 was already scanned from the assembly)
        for (int i = 0; i < 9; i++)
        {
            qorpeServices10.AddTransient<
                QorpeAbstractions.INotificationHandler<QorpePingNotification>,
                QorpePingNotificationHandler>();
        }
        _qorpeMediator10Handlers = qorpeServices10.BuildServiceProvider()
            .GetRequiredService<QorpeAbstractions.IMediator>();

        // ---- MediatR – 1 notification handler ------------------------------
        var mediatRServices1 = new ServiceCollection();
        mediatRServices1.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(currentAssembly));
        _mediatR1Handler = mediatRServices1.BuildServiceProvider()
            .GetRequiredService<global::MediatR.IMediator>();

        // ---- MediatR – 10 notification handlers ----------------------------
        var mediatRServices10 = new ServiceCollection();
        mediatRServices10.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(currentAssembly));
        // Register 9 additional handlers (1 was already scanned from the assembly)
        for (int i = 0; i < 9; i++)
        {
            mediatRServices10.AddTransient<
                INotificationHandler<MediatRPingNotification>,
                MediatRPingNotificationHandler>();
        }
        _mediatR10Handlers = mediatRServices10.BuildServiceProvider()
            .GetRequiredService<global::MediatR.IMediator>();
    }

    // ---- Send benchmarks (no pipeline behaviors, just the handler) ---------

    [Benchmark(Description = "Qorpe Send (0 behaviors)")]
    public async ValueTask<QorpeResults.Result> Qorpe_Send_NoBehaviors()
        => await _qorpeMediator1Handler.Send(_qorpePingCommand);

    [Benchmark(Description = "MediatR Send (0 behaviors)")]
    public async Task<Unit> MediatR_Send_NoBehaviors()
        => await _mediatR1Handler.Send(_mediatRPingRequest);

    // ---- Publish benchmarks – 1 handler ------------------------------------

    [Benchmark(Description = "Qorpe Publish (1 handler)")]
    public async ValueTask Qorpe_Publish_1Handler()
        => await _qorpeMediator1Handler.Publish(_qorpePingNotification);

    [Benchmark(Description = "MediatR Publish (1 handler)")]
    public async Task MediatR_Publish_1Handler()
        => await _mediatR1Handler.Publish(_mediatRPingNotification);

    // ---- Publish benchmarks – 10 handlers ----------------------------------

    [Benchmark(Description = "Qorpe Publish (10 handlers)")]
    public async ValueTask Qorpe_Publish_10Handlers()
        => await _qorpeMediator10Handlers.Publish(_qorpePingNotification);

    [Benchmark(Description = "MediatR Publish (10 handlers)")]
    public async Task MediatR_Publish_10Handlers()
        => await _mediatR10Handlers.Publish(_mediatRPingNotification);
}
