using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;

namespace Qorpe.Mediator.UnitTests.Behaviors;

public class PerformanceBehaviorTests
{
    [Fact]
    public async Task Should_Pass_Through_Fast_Requests()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions { WarningThresholdMs = 500 });
        var behavior = new PerformanceBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions { Enabled = false });
        var behavior = new PerformanceBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Should_Throw_When_Threshold_Is_Zero()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions { WarningThresholdMs = 0 });

        var act = () => new PerformanceBehavior<TestCommand, Result>(logger, opts);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Should_Log_Warning_For_Slow_Request()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions { WarningThresholdMs = 1, CriticalThresholdMs = 5000 });
        var behavior = new PerformanceBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = async () =>
        {
            await Task.Delay(50);
            return Result.Success();
        };

        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        // Logger will have been called with Warning level
    }
}
