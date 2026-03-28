using Microsoft.Extensions.DependencyInjection;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.DependencyInjection;
using Qorpe.Mediator.Exceptions;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;

namespace Qorpe.Mediator.UnitTests.Core;

public class MediatorSendTests
{
    private IMediator CreateMediator(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Send_Command_ShouldReturnSuccess()
    {
        var mediator = CreateMediator();
        var result = await mediator.Send(new TestCommand("test"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Send_CommandWithResponse_ShouldReturnValue()
    {
        var mediator = CreateMediator();
        var result = await mediator.Send(new TestCommandWithResponse("World"));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello, World!");
    }

    [Fact]
    public async Task Send_Query_ShouldReturnValue()
    {
        var mediator = CreateMediator();
        var result = await mediator.Send(new TestQuery(42));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Item-42");
    }

    [Fact]
    public async Task Send_NullRequest_ShouldThrowArgumentNull()
    {
        var mediator = CreateMediator();
        var act = async () => await mediator.Send<Result>((IRequest<Result>)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Send_WithCancelledToken_ShouldThrowOperationCancelled()
    {
        var mediator = CreateMediator();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await mediator.Send(new TestCommand("test"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

public class MediatorPublishTests
{
    [Fact]
    public async Task Publish_WithHandlers_ShouldInvokeAll()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestNotification).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Just verify it doesn't throw
        await mediator.Publish(new TestNotification("hello"));
    }

    [Fact]
    public async Task Publish_WithNoHandlers_ShouldSucceedSilently()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestNotification).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // DomainEvent with no handler — should be silent
        await mediator.Publish(new TestDomainEvent("test"));
    }

    [Fact]
    public async Task Publish_NullNotification_ShouldThrowArgumentNull()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestNotification).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var act = async () => await mediator.Publish((INotification)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

public class MediatorStreamTests
{
    [Fact]
    public async Task CreateStream_ShouldReturnAllItems()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestStreamRequest).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new TestStreamRequest(5)))
        {
            items.Add(item);
        }

        items.Should().HaveCount(5);
        items.Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4 });
    }

    [Fact]
    public async Task CreateStream_WithCancellation_ShouldStop()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestStreamRequest).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        var items = new List<int>();
        var act = async () =>
        {
            await foreach (var item in mediator.CreateStream(new TestStreamRequest(100), cts.Token))
            {
                items.Add(item);
                if (items.Count >= 3)
                {
                    cts.Cancel();
                }
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        items.Count.Should().BeGreaterThanOrEqualTo(3);
    }
}

public class MediatorConcurrencyTests
{
    [Fact]
    public async Task Send_ConcurrentRequests_ShouldNotDeadlock()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var tasks = Enumerable.Range(0, 100)
            .Select(i => mediator.Send(new TestCommand($"test-{i}")).AsTask());

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }
}
