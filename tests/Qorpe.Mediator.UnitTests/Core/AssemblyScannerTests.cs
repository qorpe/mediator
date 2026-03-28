using Microsoft.Extensions.DependencyInjection;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.DependencyInjection;
using Qorpe.Mediator.Exceptions;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;

namespace Qorpe.Mediator.UnitTests.Core;

public class AssemblyScannerDuplicateDetectionTests
{
    [Fact]
    public void Should_Report_Correct_Details_In_MultipleHandlersException()
    {
        var ex = new MultipleHandlersException(typeof(TestCommand), 2);

        ex.HandlerCount.Should().Be(2);
        ex.RequestType.Should().Be<TestCommand>();
        ex.Message.Should().Contain("Multiple handlers");
        ex.Message.Should().Contain("2");
        ex.Message.Should().Contain(typeof(TestCommand).FullName);
    }

    [Fact]
    public void Should_Not_Throw_For_Single_Handler_Per_Request()
    {
        var services = new ServiceCollection();

        // TestCommand assembly has exactly one handler per request type
        var act = () => services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));

        act.Should().NotThrow("each request type has exactly one handler");
    }

    [Fact]
    public void MultipleHandlersException_Should_Include_RequestType_And_Count()
    {
        var ex = new MultipleHandlersException(typeof(TestCommand), 3);

        ex.RequestType.Should().Be<TestCommand>();
        ex.HandlerCount.Should().Be(3);
        ex.Message.Should().Contain("3");
        ex.Message.Should().Contain(typeof(TestCommand).FullName);
    }
}

// Note: No duplicate handler types defined here — they would break all tests
// that scan the unit test assembly. Duplicate detection is tested via the
// load test assembly (multiple notification handlers) and exception validation.

public class StartupValidationTests
{
    [Fact]
    public void Should_Not_Throw_When_Validation_Disabled_Even_With_Missing_Handlers()
    {
        var services = new ServiceCollection();

        // OrphanCommand has no handler, but validation is disabled (default)
        var act = () => services.AddQorpeMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly);
            cfg.ValidateOnStartup = false;
        });

        act.Should().NotThrow("validation is disabled by default");
    }

    [Fact]
    public void Should_Throw_When_Handler_Missing_And_Validation_Enabled()
    {
        var services = new ServiceCollection();

        var act = () => services.AddQorpeMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(OrphanCommand).Assembly);
            cfg.ValidateOnStartup = true;
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OrphanCommand*")
            .WithMessage("*no registered handler*");
    }

    [Fact]
    public void Should_List_All_Missing_Handlers_In_Error_Message()
    {
        var services = new ServiceCollection();

        try
        {
            services.AddQorpeMediator(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(OrphanCommand).Assembly);
                cfg.ValidateOnStartup = true;
            });
        }
        catch (InvalidOperationException ex)
        {
            // Should mention OrphanCommand and AnotherOrphanQuery
            ex.Message.Should().Contain("OrphanCommand");
            ex.Message.Should().Contain("AnotherOrphanQuery");
            return;
        }

        Assert.Fail("Should have thrown InvalidOperationException");
    }
}

// Request types with no handler — used to test startup validation
public sealed record OrphanCommand(string Data) : ICommand<Result>;
public sealed record AnotherOrphanQuery(int Id) : IQuery<Result<string>>;
// Intentionally no handlers for these types
