using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;

namespace Qorpe.Mediator.UnitTests.Behaviors;

public class TransactionBehaviorTests
{
    private readonly ILogger<TransactionBehavior<TransactionalCommand, Result>> _logger =
        Substitute.For<ILogger<TransactionBehavior<TransactionalCommand, Result>>>();
    private readonly IOptions<TransactionBehaviorOptions> _options =
        Options.Create(new TransactionBehaviorOptions());

    [Fact]
    public async Task Should_Begin_Commit_On_Success()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await uow.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await uow.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Rollback_On_Exception()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);

        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("fail");

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await uow.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_Queries()
    {
        var queryLogger = Substitute.For<ILogger<TransactionBehavior<TestQuery, Result<string>>>>();
        var queryOpts = Options.Create(new TransactionBehaviorOptions());
        var uow = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TestQuery, Result<string>>(queryLogger, queryOpts, uow);

        RequestHandlerDelegate<Result<string>> next = () => new ValueTask<Result<string>>(Result<string>.Success("ok"));

        var result = await behavior.Handle(new TestQuery(1), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await uow.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Throw_When_No_UnitOfWork()
    {
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, unitOfWork: null);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IUnitOfWork is required*");
    }

    [Fact]
    public async Task Should_Log_Critical_When_Rollback_Fails()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(x => throw new Exception("rollback failed"));

        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);
        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("original error");

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        // Original exception should be preserved even when rollback fails
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("original error");
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var opts = Options.Create(new TransactionBehaviorOptions { Enabled = false });
        var uow = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, opts, uow);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        var result = await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await uow.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Rollback_On_Commit_Failure_Not_Handler_Failure()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("commit constraint violation"));

        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);
        var handlerCalled = false;

        RequestHandlerDelegate<Result> next = () =>
        {
            handlerCalled = true;
            return new ValueTask<Result>(Result.Success());
        };

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*commit*");
        handlerCalled.Should().BeTrue("handler should have succeeded before commit failed");
        await uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Preserve_Original_Exception_When_Rollback_Fails_After_Commit_Failure()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("commit failed"));
        uow.RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(x => throw new Exception("rollback also failed"));

        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        // Commit exception should be preserved, not the rollback one
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*commit*");
    }
}
