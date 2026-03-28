# Benchmarks

> All benchmarks use [BenchmarkDotNet](https://benchmarkdotnet.org/) with `MemoryDiagnoser` on real hardware.
> No synthetic inflation — these are honest, reproducible results.

## Environment

| Property | Value |
|----------|-------|
| **Runtime** | .NET 10.0.1 (10.0.125.57005), Arm64 RyuJIT AdvSIMD |
| **OS** | macOS (Apple Silicon, M-series) |
| **BenchmarkDotNet** | v0.14.0 |
| **MediatR Version** | v12.4.1 |
| **Qorpe.Mediator** | v1.0.0 |

---

## Send (Command/Query Pipeline)

The most critical path — every request goes through `Send()`.

| Scenario | Qorpe.Mediator | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **0 behaviors** | 25.3 ns / 64 B | 24.4 ns / 128 B | ~equal | **2x less** |
| **1 behavior** | 41.1 ns / 288 B | 56.8 ns / 368 B | **28% faster** | **22% less** |
| **3 behaviors** | 65.9 ns / 560 B | 88.0 ns / 656 B | **25% faster** | **15% less** |
| **5 behaviors** | 94.1 ns / 832 B | 116.8 ns / 944 B | **19% faster** | **12% less** |

> With behaviors (the real-world scenario), Qorpe is consistently **20-28% faster** with less memory allocation.
> The 0-behavior case is tied because both hit the same DI resolve floor (~24 ns).

## Query (Return Value)

| Scenario | Qorpe.Mediator | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **Query returning Result\<int\>** | 28.1 ns / 104 B | 27.0 ns / 200 B | ~equal | **2x less** |

> Qorpe returns `Result<int>` (richer type) with half the allocation of MediatR's raw `int`.

## Publish (Notification Fanout)

This is where Qorpe **dominates** — direct handler invocation without wrapper object allocation.

| Handlers | Qorpe.Mediator | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **1 handler** | 27.0 ns / 88 B | 43.6 ns / 288 B | **38% faster** | **3.3x less** |
| **10 handlers** | 72.6 ns / 376 B | 185.5 ns / 1,656 B | **61% faster** | **4.4x less** |
| **50 handlers** | 286 ns / 1,656 B | 813 ns / 7,736 B | **65% faster** | **4.7x less** |
| **100 handlers** | 549 ns / 3,256 B | 1,556 ns / 15,336 B | **65% faster** | **4.7x less** |

> MediatR creates `NotificationHandlerExecutor` wrapper objects + closure delegates per handler per call.
> Qorpe invokes handlers directly — zero wrapper allocation.

## Summary Scorecard

| Category | Benchmarks | Qorpe Wins | Tie | MediatR Wins |
|----------|-----------|------------|-----|--------------|
| Send (pipeline) | 4 | 3 | 1 | 0 |
| Query | 1 | 0 | 1 | 0 |
| Publish | 4 | 4 | 0 | 0 |
| **Total** | **9** | **7** | **2** | **0** |

**Memory: Qorpe uses less memory in ALL 9 benchmarks.**

---

## Why Qorpe is Faster

### 1. Typed Handler Wrappers (Send Path)
MediatR uses `MakeGenericType` + untyped `ServiceProvider.GetService(Type)` on every call.
Qorpe compiles an Expression Tree delegate on first call, then uses `ServiceProvider.GetService<T>()` — fully typed, zero reflection.

### 2. Direct Notification Dispatch (Publish Path)
MediatR creates `NotificationHandlerExecutor` objects with closure delegates for each handler on every Publish.
Qorpe detects the publisher type and invokes handlers directly via typed `foreach` — no wrapper objects, no closures.

### 3. ValueTask Over Task
When handlers complete synchronously (cache hits, validation failures), `ValueTask` avoids the `Task` heap allocation entirely. MediatR uses `Task<T>` everywhere.

### 4. ICollection Fast Path
When DI returns handlers as `ICollection<T>`, Qorpe reads `.Count` to pre-allocate exact-size arrays — no List resizing.

---

## Load Test Results

> 17 load tests covering production scenarios for banking, telecom, and healthcare workloads.

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

---

## Running Benchmarks

```bash
# BenchmarkDotNet comparison vs MediatR
cd tests/Qorpe.Mediator.Benchmarks
dotnet run -c Release

# Load tests
dotnet test tests/Qorpe.Mediator.LoadTests
```
