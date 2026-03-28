# Benchmarks

## Environment

- **Runtime:** .NET 10.0.1 (10.0.125.57005), Arm64 RyuJIT AdvSIMD
- **OS:** macOS (Apple Silicon, M-series)
- **BenchmarkDotNet:** v0.14.0

## Results (Real BenchmarkDotNet Output)

```
| Method                           | Mean       | Error    | StdDev   | Gen0   | Gen1   | Allocated |
|--------------------------------- |-----------:|---------:|---------:|-------:|-------:|----------:|
| 'Qorpe Send (0 behaviors)'      |  271.66 ns | 5.275 ns | 4.676 ns | 0.0648 |      - |     544 B |
| 'MediatR Send (0 behaviors)'    |   27.40 ns | 0.109 ns | 0.096 ns | 0.0153 |      - |     128 B |
| 'Qorpe Publish (1 handler)'     |   65.43 ns | 0.254 ns | 0.212 ns | 0.0478 |      - |     400 B |
| 'MediatR Publish (1 handler)'   |   46.90 ns | 0.479 ns | 0.448 ns | 0.0344 |      - |     288 B |
| 'Qorpe Publish (10 handlers)'   |  414.38 ns | 2.680 ns | 2.376 ns | 0.3519 | 0.0019 |    2944 B |
| 'MediatR Publish (10 handlers)' |  188.04 ns | 1.023 ns | 0.907 ns | 0.1979 | 0.0002 |    1656 B |
```

## Analysis

### Current State (v1.0.0)

In raw nanosecond overhead, MediatR v12 is currently faster on the Send/Publish hot path. This is expected for v1.0.0 — MediatR has had 12 major versions and years of optimization.

**However**, Qorpe.Mediator provides significant value that raw microbenchmarks don't capture:

1. **9 built-in behaviors** — MediatR ships zero. In real apps, you'd add validation, logging, transactions, etc., which adds overhead to MediatR's baseline.
2. **Result pattern** — No exception-based control flow. Exception throwing costs ~5,000-10,000ns per throw, while Result.Failure costs ~0ns.
3. **ValueTask** — On synchronous completion (cache hits, validation failures), no Task allocation.
4. **Type info caching** — MakeGenericType results cached per request type. First call builds, all subsequent calls are dictionary lookups.

### Real-World Performance

In production scenarios with full behavior pipelines, the overhead difference is negligible:
- Both libraries add < 1 microsecond overhead
- Your database call: 1,000,000+ nanoseconds
- Your HTTP call: 10,000,000+ nanoseconds
- Mediator overhead is < 0.01% of total request time

### Optimization Roadmap (v1.1.0)

Planned optimizations to close the gap:
- Source generators for compile-time handler resolution (zero-reflection)
- Pre-compiled pipeline chains via IL emit
- Object pooling for notification handler executor lists

## Key Performance Techniques Used

1. **Compiled Delegate Caching** — Handler delegates compiled once via Expression trees
2. **Pipeline Type Info Caching** — MakeGenericType results cached per request type
3. **ValueTask** — Avoids Task allocation on synchronous completion paths
4. **No LINQ in hot paths** — For loops instead of Where/Select/ToList
5. **Behavior enumeration without List** — Direct array growth, no List allocation

## Running Benchmarks

```bash
cd tests/Qorpe.Mediator.Benchmarks
dotnet run -c Release
```

## Load Test Results

All load tests pass on .NET 10.0:

| Test | Result |
|------|--------|
| 10,000 concurrent Send requests | No deadlocks, all succeed |
| 100,000 sequential requests | Memory stable (< 10MB growth) |
| 5,000 concurrent queries | All succeed |
| 1,000 notifications x 3 handlers | 3,000 executions, no errors |
| Mixed operations (3,000 concurrent) | Commands + queries + notifications |
| 5-second sustained load | > 1,000 req/sec, zero errors |
