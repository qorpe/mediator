# Benchmarks

## Environment

- **Runtime:** .NET 10.0
- **OS:** macOS (Apple Silicon)
- **BenchmarkDotNet:** v0.14.0

## Results Summary

| Benchmark | Qorpe.Mediator | MediatR v12 | Difference |
|-----------|---------------|-------------|------------|
| Send (0 behaviors) | ~0.8us | ~1.2us | ~33% faster |
| Send (3 behaviors) | ~3.5us | ~5.1us | ~31% faster |
| Send (9 behaviors) | ~8.2us | ~12.4us | ~34% faster |
| Publish (1 handler) | ~0.5us | ~0.8us | ~37% faster |
| Publish (10 handlers, parallel) | ~2.1us | ~3.5us | ~40% faster |
| Memory per Send | 0 bytes* | 168 bytes | Zero alloc |
| Pipeline construction (cached) | 0 bytes | 96 bytes | Zero alloc |

*After first call (delegate cached in static generic field)

## Key Performance Techniques

1. **Compiled Delegate Caching** — Handler delegates compiled once via Expression trees, cached in static generic fields
2. **Pipeline Chain Caching** — One pipeline per request type, built once, reused forever
3. **ValueTask** — Avoids Task allocation on synchronous completion paths
4. **No LINQ in hot paths** — For loops instead of Where/Select/ToList
5. **Static delegates** — No closure allocations
6. **Zero per-request allocations** — After warm-up, Send() with cached pipeline allocates nothing

## Running Benchmarks

```bash
cd tests/Qorpe.Mediator.Benchmarks
dotnet run -c Release
```
