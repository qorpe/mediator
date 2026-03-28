using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Audit;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;

namespace Qorpe.Mediator.UnitTests.Behaviors;

public class AuditBehaviorTests
{
    private readonly InMemoryAuditStore _store = new();
    private readonly ILogger<AuditBehavior<TransactionalCommand, Result>> _logger =
        Substitute.For<ILogger<AuditBehavior<TransactionalCommand, Result>>>();

    private AuditBehavior<TransactionalCommand, Result> CreateBehavior(
        AuditBehaviorOptions? opts = null,
        IAuditUserContext? userContext = null)
    {
        var options = Options.Create(opts ?? new AuditBehaviorOptions { AuditCommands = true });
        return new AuditBehavior<TransactionalCommand, Result>(_store, _logger, options, userContext);
    }

    [Fact]
    public async Task Should_Create_Audit_Entry_On_Success()
    {
        var behavior = CreateBehavior();
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        var entries = _store.GetAll();
        entries.Should().ContainSingle();
        entries[0].IsSuccess.Should().BeTrue();
        entries[0].RequestType.Should().Contain("TransactionalCommand");
        entries[0].DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Should_Create_Audit_Entry_On_Failure()
    {
        var behavior = CreateBehavior();
        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("boom");

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var entries = _store.GetAll();
        entries.Should().ContainSingle();
        entries[0].IsSuccess.Should().BeFalse();
        entries[0].ErrorMessage.Should().Be("boom");
        entries[0].ExceptionType.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task Should_Use_SYSTEM_When_No_User_Context()
    {
        var behavior = CreateBehavior(userContext: null);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        _store.GetAll()[0].UserId.Should().Be("SYSTEM");
    }

    [Fact]
    public async Task Should_Use_User_From_Context()
    {
        var userCtx = Substitute.For<IAuditUserContext>();
        userCtx.GetUserId().Returns("user-42");
        userCtx.GetUserName().Returns("John Doe");

        var behavior = CreateBehavior(userContext: userCtx);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        var entry = _store.GetAll()[0];
        entry.UserId.Should().Be("user-42");
        entry.UserName.Should().Be("John Doe");
    }

    [Fact]
    public async Task Should_Mask_Sensitive_Properties_In_Request_Data()
    {
        var sensitiveLogger = Substitute.For<ILogger<AuditBehavior<SensitiveCommand, Result>>>();
        var opts = new AuditBehaviorOptions { AuditCommands = true };
        opts.SensitivePatterns.Add("Password");
        var behavior = new AuditBehavior<SensitiveCommand, Result>(
            _store, sensitiveLogger, Options.Create(opts));

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new SensitiveCommand("John", "secret", "4111"), next, CancellationToken.None);

        var entry = _store.GetAll()[0];
        entry.RequestData.Should().NotBeNull();
        entry.RequestData.Should().Contain("***"); // Password masked
        entry.RequestData.Should().NotContain("secret");
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var behavior = CreateBehavior(new AuditBehaviorOptions { Enabled = false });
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        _store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Should_Skip_Queries_When_AuditQueries_False()
    {
        var queryLogger = Substitute.For<ILogger<AuditBehavior<TestQuery, Result<string>>>>();
        var opts = new AuditBehaviorOptions { AuditCommands = true, AuditQueries = false };
        var behavior = new AuditBehavior<TestQuery, Result<string>>(
            _store, queryLogger, Options.Create(opts));

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        await behavior.Handle(new TestQuery(1), next, CancellationToken.None);

        _store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Should_Not_Fail_Request_When_Store_Throws()
    {
        var failingStore = Substitute.For<IAuditStore>();
        failingStore.SaveAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(x => throw new Exception("store down"));

        var opts = new AuditBehaviorOptions { AuditCommands = true, FallbackToConsole = true };
        var behavior = new AuditBehavior<TransactionalCommand, Result>(
            failingStore, _logger, Options.Create(opts));

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        // Should NOT throw even though audit store fails
        var result = await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Generate_CorrelationId_When_No_Activity()
    {
        var behavior = CreateBehavior();
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        _store.GetAll()[0].CorrelationId.Should().NotBeNullOrEmpty();
    }
}
