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
| **Qorpe.Mediator** | v1.0.0-preview.4 |

## Send (Command/Query Pipeline)

The most critical path — every request goes through `Send()`.

| Scenario | Qorpe.Mediator | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **0 behaviors** | 58 ns / 192 B | 27 ns / 128 B | MediatR faster | Qorpe +64 B |
| **1 behavior** | 83 ns / 352 B | 62 ns / 368 B | MediatR faster | **Qorpe 4% less** |
| **3 behaviors** | 110 ns / 624 B | 94 ns / 656 B | MediatR faster | **Qorpe 5% less** |
| **5 behaviors** | 135 ns / 896 B | 123 ns / 944 B | MediatR faster | **Qorpe 5% less** |

> The Send path has slightly higher latency due to pre/post processor resolution and behavior ordering.
> Memory allocation is consistently lower with behaviors due to typed pipeline construction.
> This trade-off enables pre-processors, post-processors, and explicit behavior ordering — features MediatR does not have.

## Query (Return Value)

| Scenario | Qorpe.Mediator | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **Query returning Result\<int\>** | 62 ns / 232 B | 30 ns / 200 B | MediatR faster | Qorpe +32 B |

> Qorpe returns `Result<int>` (richer type with error handling) vs MediatR's raw `int`.

## Publish (Notification Fanout)

This is where Qorpe excels — direct handler invocation without wrapper object allocation.

| Handlers | Qorpe.Mediator | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **1 handler** | 27 ns / 88 B | 50 ns / 288 B | **47% faster** | **3.3x less** |
| **10 handlers** | 75 ns / 376 B | 205 ns / 1,656 B | **63% faster** | **4.4x less** |
| **50 handlers** | 290 ns / 1,656 B | 847 ns / 7,736 B | **66% faster** | **4.7x less** |
| **100 handlers** | 578 ns / 3,256 B | 1,722 ns / 15,336 B | **66% faster** | **4.7x less** |

> MediatR creates `NotificationHandlerExecutor` wrapper objects + closure delegates per handler per call.
> Qorpe invokes handlers directly — zero wrapper allocation.

## Summary Scorecard

| Category | Benchmarks | Qorpe Wins | MediatR Wins |
|----------|-----------|------------|--------------|
| Send (pipeline) | 4 | 0 (lower memory) | 4 (lower latency) |
| Query | 1 | 0 | 1 |
| Publish | 4 | 4 | 0 |
| **Total** | **9** | **4** | **5** |

**Publish path: Qorpe is 47-66% faster with 3.3-4.7x less memory.**
**Send path: MediatR has lower latency; Qorpe has lower memory and richer feature set (pre/post processors, behavior ordering, cancellation diagnostics).**

## Why the Trade-off

Qorpe's Send path resolves pre-processors, post-processors, and sorts behaviors by `IBehaviorOrder` on each request. These features add ~30-40 ns overhead but enable:

- `IRequestPreProcessor<T>` / `IRequestPostProcessor<T,R>` execution
- Explicit behavior ordering via `IBehaviorOrder`
- Cancellation diagnostics with pipeline stage tracking
- Per-request performance threshold attributes

MediatR does not offer these features. The overhead is negligible in real-world scenarios where handler execution dominates (typically 1-100+ ms).

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
