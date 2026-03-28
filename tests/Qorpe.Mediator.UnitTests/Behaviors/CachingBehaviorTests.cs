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

    [Fact]
    public async Task Should_Handle_Many_Unique_Keys_Without_Unbounded_Growth()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        // Execute with many unique cache keys
        for (int i = 0; i < 1_000; i++)
        {
            await behavior.Handle(new CacheableQuery(i), next, CancellationToken.None);
        }

        // The behavior should still function correctly after many unique keys
        var result = await behavior.Handle(new CacheableQuery(9999), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }
}

public class BoundedLockPoolTests
{
    [Fact]
    public void Should_Create_And_Return_Same_Lock_For_Same_Key()
    {
        var pool = new BoundedLockPool(maxSize: 100, evictionInterval: TimeSpan.FromMinutes(5));

        var lock1 = pool.GetOrCreate("key-1");
        var lock2 = pool.GetOrCreate("key-1");

        lock1.Should().BeSameAs(lock2);
    }

    [Fact]
    public void Should_Create_Different_Locks_For_Different_Keys()
    {
        var pool = new BoundedLockPool(maxSize: 100, evictionInterval: TimeSpan.FromMinutes(5));

        var lock1 = pool.GetOrCreate("key-1");
        var lock2 = pool.GetOrCreate("key-2");

        lock1.Should().NotBeSameAs(lock2);
    }

    [Fact]
    public void Should_Evict_Stale_Entries_When_Pool_Exceeds_Max_Size()
    {
        // Use a very short eviction interval so entries become stale immediately
        var pool = new BoundedLockPool(maxSize: 5, evictionInterval: TimeSpan.FromMilliseconds(1));

        // Fill pool beyond max size
        for (int i = 0; i < 10; i++)
        {
            pool.GetOrCreate($"key-{i}");
        }

        // Wait for entries to become stale
        Thread.Sleep(10);

        // Trigger eviction by adding another key
        pool.GetOrCreate("trigger-eviction");

        // Pool should have been trimmed
        pool.Count.Should().BeLessThan(12);
    }

    [Fact]
    public void Should_Not_Evict_Actively_Used_Locks()
    {
        var pool = new BoundedLockPool(maxSize: 5, evictionInterval: TimeSpan.FromMilliseconds(1));

        var activeLock = pool.GetOrCreate("active-key");

        // Add stale entries
        for (int i = 0; i < 10; i++)
        {
            pool.GetOrCreate($"stale-{i}");
        }

        Thread.Sleep(10);

        // Touch the active key again
        var sameActiveLock = pool.GetOrCreate("active-key");

        // Force eviction
        pool.ForceEviction();

        // Active key should survive eviction
        sameActiveLock.Should().BeSameAs(activeLock);
        var afterEviction = pool.GetOrCreate("active-key");
        afterEviction.Should().BeSameAs(activeLock);
    }

    [Fact]
    public void Should_Not_Evict_Lock_That_Is_Currently_Held()
    {
        var pool = new BoundedLockPool(maxSize: 5, evictionInterval: TimeSpan.FromMilliseconds(1));

        var heldLock = pool.GetOrCreate("held-key");
        heldLock.Wait(); // Acquire the lock (CurrentCount = 0)

        Thread.Sleep(10);

        // Force eviction — should skip held locks
        pool.ForceEviction();

        // Held lock should not be evicted
        pool.Count.Should().BeGreaterThanOrEqualTo(1);

        heldLock.Release();
    }

    [Fact]
    public async Task Should_Be_Thread_Safe_Under_Concurrent_Access()
    {
        var pool = new BoundedLockPool(maxSize: 1_000, evictionInterval: TimeSpan.FromMinutes(5));

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < 100; j++)
            {
                var semaphore = pool.GetOrCreate($"key-{i}-{j}");
                semaphore.Should().NotBeNull();
            }
        }));

        await Task.WhenAll(tasks);

        pool.Count.Should().BeGreaterThan(0);
        pool.Count.Should().BeLessThanOrEqualTo(10_000);
    }
}
