# Benchmarks

> All benchmarks use [BenchmarkDotNet](https://benchmarkdotnet.org/) with `MemoryDiagnoser` on real hardware.
> No synthetic inflation — these are honest, reproducible results.

## Environment

| Property | Value |
|----------|-------|
| **Runtime** | .NET 10.0.1 (10.0.125.57005), Arm64 RyuJIT AdvSIMD |
| **OS** | macOS (Apple M5) |
| **BenchmarkDotNet** | v0.14.0 |
| **MediatR Version** | v12.4.1 |
| **Qorpe.Mediator** | v1.0.0-preview.4+ |

## Send (Command/Query Pipeline)

The most critical path — every request goes through `Send()`.

| Scenario | Qorpe.Mediator | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **0 behaviors** | 46 ns / 128 B | 25 ns / 128 B | MediatR faster | **Equal** |
| **1 behavior** | 61 ns / 288 B | 59 ns / 368 B | ~equal | **Qorpe 22% less** |
| **3 behaviors** | 89 ns / 560 B | 90 ns / 656 B | **Qorpe 1% faster** | **Qorpe 15% less** |
| **5 behaviors** | 119 ns / 832 B | 118 ns / 944 B | ~equal | **Qorpe 12% less** |

> With behaviors (the real-world scenario), Qorpe matches or beats MediatR in speed while using consistently less memory.
> The 0-behavior gap (~20 ns) comes from pre/post processor infrastructure — features MediatR does not offer.

## Query (Return Value)

| Scenario | Qorpe.Mediator | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **Query returning Result\<int\>** | 51 ns / 168 B | 28 ns / 200 B | MediatR faster | **Qorpe 16% less** |

> Qorpe returns `Result<int>` (richer type with error handling) vs MediatR's raw `int`.

## Publish (Notification Fanout)

This is where Qorpe dominates — direct handler invocation without wrapper object allocation.

| Handlers | Qorpe.Mediator | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **1 handler** | 24 ns / 88 B | 46 ns / 288 B | **48% faster** | **3.3x less** |
| **10 handlers** | 69 ns / 376 B | 187 ns / 1,656 B | **63% faster** | **4.4x less** |
| **50 handlers** | 273 ns / 1,656 B | 791 ns / 7,736 B | **65% faster** | **4.7x less** |
| **100 handlers** | 532 ns / 3,256 B | 1,552 ns / 15,336 B | **66% faster** | **4.7x less** |

> MediatR creates `NotificationHandlerExecutor` wrapper objects + closure delegates per handler per call.
> Qorpe invokes handlers directly — zero wrapper allocation.

## Summary Scorecard

| Category | Benchmarks | Qorpe Wins | Tie | MediatR Wins |
|----------|-----------|------------|-----|--------------|
| Send (with behaviors) | 3 | 1 | 2 | 0 |
| Send (0 behaviors) | 1 | 0 | 0 | 1 |
| Query | 1 | 0 | 0 | 1 |
| Publish | 4 | 4 | 0 | 0 |
| **Total** | **9** | **5** | **2** | **2** |

**Memory: Qorpe uses equal or less memory in all 9 benchmarks.**
**Publish: 48-66% faster with 3.3-4.7x less memory.**
**Send with behaviors: equal or faster speed, 12-22% less memory.**

## Load Test Results

> 18 load tests covering production scenarios.

### Concurrency and Throughput

| Test | Scale | Result |
|------|-------|--------|
| Concurrent Send | 10,000 simultaneous | No deadlocks, all succeed |
| Concurrent Send + Behaviors | 50,000 simultaneous | No deadlocks, all succeed |
| Concurrent Query | 5,000 simultaneous | All succeed, correct results |
| Scoped DI (per-request) | 10,000 scopes | All succeed independently |
| Mixed Operations | 10,000 (cmd + query + notification) | All complete cleanly |

### Memory and Stability

| Test | Scale | Result |
|------|-------|--------|
| Sequential Memory Leak | 100,000 requests | < 10 MB growth |
| Sequential + Behaviors Memory | 500,000 requests | < 20 MB growth |
| Caching High-Cardinality Keys | 10,000 unique keys | < 20 MB growth |
| Thread Pool Exhaustion | 20,000 operations | Pool not depleted |

### Notification Fanout

| Test | Scale | Result |
|------|-------|--------|
| Sequential Fanout | 1,000 x 3 handlers = 3,000 executions | All succeed |
| Parallel Fanout | 5,000 x 10 handlers = 50,000 executions | All succeed |

### Resilience

| Test | Scale | Result |
|------|-------|--------|
| Exception Under Load | 10,000 (33% failures) | All complete, no leaks |
| Cancellation Mid-Flight | 5,000 + cancel | No hanging tasks |
| Graceful Degradation | 10,000 mixed success/fail/cancel | All complete |
| Re-entrant Send | 1,000 x depth 3 = 4,000 nested calls | No deadlocks |

### Streaming

| Test | Scale | Result |
|------|-------|--------|
| Concurrent Consumers | 100 consumers x 1,000 items = 100,000 items | All correct |

### Latency Percentiles (10-second sustained)

| Percentile | Latency |
|------------|---------|
| **p50** | < 1 ms |
| **p95** | < 5 ms |
| **p99** | < 10 ms |
| **Throughput** | > 10,000 req/sec |

## Running Benchmarks

```bash
# BenchmarkDotNet comparison vs MediatR
cd tests/Qorpe.Mediator.Benchmarks
dotnet run -c Release

# Load tests
dotnet test tests/Qorpe.Mediator.LoadTests
```
