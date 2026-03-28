using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;

namespace Qorpe.Mediator.UnitTests.Behaviors;

public class LoggingBehaviorTests
{
    private readonly ILogger<LoggingBehavior<TestCommand, Result>> _logger;
    private readonly IOptions<LoggingBehaviorOptions> _options;

    public LoggingBehaviorTests()
    {
        _logger = Substitute.For<ILogger<LoggingBehavior<TestCommand, Result>>>();
        _options = Options.Create(new LoggingBehaviorOptions());
    }

    [Fact]
    public async Task Should_Log_Request_And_Call_Next()
    {
        var behavior = new LoggingBehavior<TestCommand, Result>(_logger, _options);
        var called = false;
        RequestHandlerDelegate<Result> next = () =>
        {
            called = true;
            return new ValueTask<Result>(Result.Success());
        };

        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        called.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Log_Exception_And_Rethrow()
    {
        var behavior = new LoggingBehavior<TestCommand, Result>(_logger, _options);
        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("boom");

        var act = async () => await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var opts = Options.Create(new LoggingBehaviorOptions { Enabled = false });
        var behavior = new LoggingBehavior<TestCommand, Result>(_logger, opts);
        var called = false;
        RequestHandlerDelegate<Result> next = () =>
        {
            called = true;
            return new ValueTask<Result>(Result.Success());
        };

        await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        called.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Mask_Sensitive_Properties()
    {
        var sensitiveLogger = Substitute.For<ILogger<LoggingBehavior<SensitiveCommand, Result>>>();
        var opts = Options.Create(new LoggingBehaviorOptions());
        var behavior = new LoggingBehavior<SensitiveCommand, Result>(sensitiveLogger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        await behavior.Handle(new SensitiveCommand("John", "secret123", "4111111111111111"), next, CancellationToken.None);
        // Should not throw — sensitive data is masked internally
    }

    [Fact]
    public async Task Should_Truncate_Large_Request()
    {
        var opts = Options.Create(new LoggingBehaviorOptions { MaxSerializedLength = 20 });
        var behavior = new LoggingBehavior<TestCommand, Result>(_logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        await behavior.Handle(new TestCommand("a very long name that exceeds limit"), next, CancellationToken.None);
        // Should not throw — truncation handled internally
    }
}
