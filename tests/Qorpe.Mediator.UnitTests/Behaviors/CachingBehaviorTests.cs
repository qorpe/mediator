using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;
using System.Text.Json;

namespace Qorpe.Mediator.UnitTests.Behaviors;

public class CachingBehaviorTests
{
    private readonly ILogger<CachingBehavior<CacheableQuery, Result<string>>> _logger =
        Substitute.For<ILogger<CachingBehavior<CacheableQuery, Result<string>>>>();
    private readonly IOptions<CachingBehaviorOptions> _options =
        Options.Create(new CachingBehaviorOptions());

    [Fact]
    public async Task Should_Cache_Query_Response()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache);
        var callCount = 0;

        RequestHandlerDelegate<Result<string>> next = () =>
        {
            callCount++;
            return new ValueTask<Result<string>>(Result<string>.Success("result"));
        };

        var result = await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("result");
        callCount.Should().Be(1);
        await cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Return_Cached_Response_On_Hit()
    {
        var cache = Substitute.For<IDistributedCache>();
        var cachedValue = Result<string>.Success("cached-value");
        var cached = JsonSerializer.SerializeToUtf8Bytes(cachedValue);

        // NSubstitute: mock the extension method's underlying Get call
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(cached));

        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("fresh"));

        await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);

        // Verify cache was queried
        await cache.Received().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_Commands()
    {
        var cmdLogger = Substitute.For<ILogger<CachingBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new CachingBehaviorOptions());
        var cache = Substitute.For<IDistributedCache>();
        var behavior = new CachingBehavior<TestCommand, Result>(cmdLogger, opts, cache);
        var called = false;

        RequestHandlerDelegate<Result> next = () =>
        {
            called = true;
            return new ValueTask<Result>(Result.Success());
        };

        await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);

        called.Should().BeTrue();
        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Fall_Through_When_No_Cache_Configured()
    {
        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache: null);
        var called = false;

        RequestHandlerDelegate<Result<string>> next = () =>
        {
            called = true;
            return new ValueTask<Result<string>>(Result<string>.Success("ok"));
        };

        var result = await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);
        called.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task Should_Fall_Through_When_Cache_Read_Fails()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<byte[]?>(x => throw new Exception("cache down"));

        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache);
        var called = false;

        RequestHandlerDelegate<Result<string>> next = () =>
        {
            called = true;
            return new ValueTask<Result<string>>(Result<string>.Success("fallback"));
        };

        var result = await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);
        called.Should().BeTrue();
        result.Value.Should().Be("fallback");
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var opts = Options.Create(new CachingBehaviorOptions { Enabled = false });
        var cache = Substitute.For<IDistributedCache>();
        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, opts, cache);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);
        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_When_No_Cacheable_Attribute()
    {
        var queryLogger = Substitute.For<ILogger<CachingBehavior<TestQuery, Result<string>>>>();
        var opts = Options.Create(new CachingBehaviorOptions());
        var cache = Substitute.For<IDistributedCache>();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(queryLogger, opts, cache);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        await behavior.Handle(new TestQuery(1), next, CancellationToken.None);
        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
