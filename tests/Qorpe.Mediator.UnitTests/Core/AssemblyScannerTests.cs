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
