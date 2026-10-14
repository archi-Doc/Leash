# Benchmark results

Measured on 2026-10-14 with .NET SDK 11.0.100-rc.2 building for the .NET 10.0.13 runtime, BenchmarkDotNet 0.15.8, Windows 11, and an Intel Core Ultra X7 368H. The BenchmarkDotNet run used one launch, three warmup iterations, nine measurement iterations, and an affinity mask of the first eight cores.

```sh
dotnet run -c Release --project Benchmark -- --filter "*TraverseParsedTree*" --job short --warmupCount 3 --iterationCount 9 --affinity 255
```

The input contains the requested number of methods plus one empty helper. Each ordinary method has 20 calls. Dense methods contain one call with 200 additions. Nested methods contain 32 nested local functions and one call. Parsing is excluded. The benchmark keeps every red node alive, so Roslyn's weakly held statement lists are not collected and re-materialized between iterations; the figures below therefore measure the traversal engine alone.

The previous engine is the one at commit `6a82e0c`. For this comparison, a temporary benchmark ran both engines in the same BenchmarkDotNet session on the same retained trees and with the settings above; the command above reproduces the current-engine column.

| Methods | Shape | Previous engine | Current engine | Ratio | Allocated |
| ---: | --- | ---: | ---: | ---: | ---: |
| 10 | Flat | 23.42 us | 24.05 us | 1.03 | 0 B |
| 10 | Dense | 90.75 us | 84.82 us | 0.93 | 0 B |
| 10 | Nested | 86.65 us | 77.33 us | 0.91 | 0 B |
| 100 | Flat | 324.94 us | 193.80 us | 0.61 | 0 B |
| 100 | Dense | 755.52 us | 552.21 us | 0.73 | 0 B |
| 100 | Nested | 808.47 us | 654.85 us | 0.81 | 0 B |
| 1,000 | Flat | 2.656 ms | 2.212 ms | 0.83 | 0 B |
| 1,000 | Dense | 8.648 ms | 7.277 ms | 0.84 | 0 B |
| 1,000 | Nested | 8.985 ms | 6.862 ms | 0.77 | 0 B |

Time grows linearly with input size for all three shapes, and neither engine allocates during traversal: the stack is reused per thread, and names and numeric strings are created only when a diagnostic is reported. Several BenchmarkDotNet rows have wide error intervals on this hybrid-core machine (the 100-method flat ratio in particular), so use the in-process ratios below as the conservative figures.

## Changes in the current engine

- Each node is classified by one read from a table indexed by syntax kind instead of a large `switch`. Kinds without a fixed class are resolved from their node type on first use and cached.
- Single-token nodes (names, predefined types, literals, `this`, `base`, omitted sizes and type arguments, designations, interpolated text, and discard patterns) are folded into their parent without a stack frame, using the node's missing flag instead of reading its token.
- A function's block body, expression body, constructor initializer, member initializer value, and top-level statements accumulate into one unit counter. The stack frame shrank from 144 to 112 bytes, and error-recovery declarations with both a block and an expression body are measured completely.
- The analyzer passes its syntax-tree analysis context by value through a small struct instead of creating a reporting delegate, which removes the boxed context and delegate that were allocated for every syntax tree.

## In-process comparison

An interleaved in-process comparison ran both engines on the same retained trees with fully optimized code (`DOTNET_TieredCompilation=0`), on one pinned core, taking the minimum of 25 rounds. Three runs gave these ratios of previous to current traversal time:

| Input | Ratio |
| --- | ---: |
| 3,700 C# files from local repositories (about 9.5 million nodes and tokens, 49,300 units) | 1.12x–1.17x |
| 1,000 flat methods | 1.17x–1.25x |
| 1,000 dense methods | 1.13x–1.19x |
| 1,000 nested methods | 1.16x–1.20x |

Both engines produced identical measurements for every unit of the corpus and the synthetic inputs. The two engines differ only on error-recovery syntax, which the new regression tests cover.

A reference walker that only enumerates every child with Roslyn's `ChildSyntaxList` and counts tokens took 201 ms on the corpus, while the current engine took 177 ms because it creates no enumerator for single-token nodes. Traversal time is therefore dominated by Roslyn's red-tree enumeration; the public syntax API offers no cheaper way to visit every token.

## Measurement notes

The machine has a hybrid core layout. Short runs can land on efficiency cores and vary by about 2x between launches; pin with `--affinity` or repeat the run before comparing small differences. Pinning an in-process harness to a single core while raising its thread priority starves the tiered JIT and leaves code unoptimized, so in-process comparisons should disable tiered compilation instead. Larger inputs plus profiling are appropriate before drawing conclusions about production workloads.

The solution passed 210 xUnit cases, including a test that traversal of a retained tree allocates nothing. Raw benchmark data is written under `BenchmarkDotNet.Artifacts/`.
