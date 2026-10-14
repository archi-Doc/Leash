# Leash

A syntax-only C# analyzer for implementation size and parameter limits. The normative rules are in [SPEC.md](SPEC.md).

```text
LEASH0001: 'Foo' is 58 logical lines long (limit 50). Statements: 42, elements: 573.
```

An element is a single syntax token: an identifier, keyword, operator, punctuation, or literal.

Logical lines are `max(weighted statement count, ceiling(elements / 10))`. `Statements` is a weighted count of declarations, assignments, increments, and control structures, not a raw count of C# statement nodes. Comments and formatting do not contribute. A value equal to its limit is allowed.

| Project | Purpose |
| --- | --- |
| `Leash` | Roslyn analyzer and exemption-attribute generator; `netstandard2.0`, Roslyn 4.14 |
| `xUnitTest` | Counting, diagnostics, configuration, exemption, generator, concurrency, and host tests; .NET 10 |
| `Benchmark` | BenchmarkDotNet timing and memory measurements; .NET 10 |

## Build and test

Install .NET SDK 10.0.100 or later in the 10.0 SDK family. `global.json` selects the Microsoft.Testing.Platform test runner.

```sh
dotnet restore Leash.slnx
dotnet build Leash.slnx -c Release --no-restore
dotnet test --solution Leash.slnx -c Release --no-build
dotnet pack Leash/Leash.csproj -c Release --no-build -o artifacts/packages
```

The analyzer requires a host with Roslyn 4.14 or later. The supported syntax baseline is C# 13; tests use that language version. The analyzer's build language is C# 13 and its runtime target is .NET Standard 2.0. The SDK command-line host is verified separately from IDE support; IDE compatibility must be tested before claiming a supported IDE release.

## Use in another project

For a local project reference:

```xml
<ItemGroup>
  <ProjectReference Include="../Leash/Leash.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Alternatively, install the locally packed NuGet package from your package source with `PrivateAssets="all"`. Its analyzer assembly is placed under `analyzers/dotnet/cs`.

```ini
[*.cs]
leash.max_lines = 50
leash.max_parameters = 6
leash.max_out_parameters = 1
leash.allow_exempt = true
```

| Rule | Meaning |
| --- | --- |
| [LEASH0001](docs/LEASH0001.md) | Too many logical lines |
| [LEASH0002](docs/LEASH0002.md) | Too many explicit parameters |
| [LEASH0003](docs/LEASH0003.md) | Too many explicit `out` parameters |
| [LEASH0004](docs/LEASH0004.md) | Invalid effective configuration |
| [LEASH0005](docs/LEASH0005.md) | Invalid or disabled exemption |

All diagnostics are errors tagged `NotConfigurable`. Severity overrides, `NoWarn`, source pragmas, and diagnostic suppressors cannot disable them. Use the numeric options to change limits. Invalid options produce errors rather than silently using defaults. Generated code follows Roslyn's classification.

**Host limitation:** Roslyn 4.14 still honors `SuppressMessage` and `UnconditionalSuppressMessage` attributes for these diagnostics. Prohibit those attributes for Leash IDs through build policy and review. Tests record this behavior explicitly; see [the enforcement boundary](SPEC.md#63-enforcement-boundary). Attribute suppression cannot be absolutely prevented by this syntax-only analyzer through the supported diagnostic API.

## Reasoned exemptions

The package adds `Leash.LeashExemptAttribute` to every project that uses it, through a source generator in the analyzer assembly; no attribute definition is needed.

```csharp
using Leash;

[LeashExempt("External contract requires this signature.")]
public void Execute(int a, int b, int c, int d, int e, int f, int g) { }
```

The generated type is `internal` and marked `[Microsoft.CodeAnalysis.Embedded]`, so projects connected by `InternalsVisibleTo` do not see each other's copies. It is also `[Conditional("LEASH_ATTRIBUTES")]`, so attribute usages are not emitted to metadata unless that symbol is defined. A project that already defines its own `LeashExemptAttribute` keeps compiling, because the generated type is visible only where the `Leash` namespace is imported. A type in the global namespace or the file's own namespace takes precedence over the imported one; if a file imports both namespaces with `using` directives, qualify the attribute name.

Only the written attribute name and a non-blank string literal reason are inspected. No symbols or aliases are resolved. Exempting a nested function does not reduce its parent's size; a parent's exemption does not exempt nested functions. See [LEASH0005](docs/LEASH0005.md) for supported targets.

## Benchmarks and implementation

```sh
dotnet run -c Release --project Benchmark -- --filter "*TraverseParsedTree*" --job short
dotnet run -c Release --project Benchmark -- --filter "*AnalyzeWithRoslyn*" --job short
```

The benchmark varies method count (10, 100, 1,000) and structure (ordinary statements, dense expressions, nested local functions). `TraverseParsedTree` excludes parsing, keeps every red node alive so that Roslyn's weakly held statement lists are not re-materialized between iterations, and uses limits that avoid diagnostic allocations; it measures the engine alone. `AnalyzeWithRoslyn` exercises the normal analyzer-driver pipeline under the default limits. `MemoryDiagnoser` reports allocations; the traversal benchmark allocates nothing because the engine reuses its stack and the tree is retained. Use `--job Dry` for a quick execution check, not a reliable performance result.

The engine visits each syntax node/token once, aggregates completed child measurements into parents, and uses a reusable per-thread explicit stack instead of recursion. Each node is classified by one read from a table indexed by syntax kind, single-token nodes such as identifiers, literals, and designations are folded into their parent without a stack frame, and the analyzer assembly depends on nothing beyond Roslyn. It retains O(depth) traversal state, checks cancellation, and clears node references before caching the stack. Counters are 64-bit; logical-line rounding avoids an overflowing addition. The analysis context is passed by value rather than through a reporting delegate, so apart from the per-thread stack, the analyzer allocates nothing per syntax tree until it reports a diagnostic; Roslyn may still materialize red nodes during the traversal.

See [the measured baseline](docs/BenchmarkResults.md) for timings, allocations, and measurement limits.

Diagnostic help pages are included in the package. The public `helpLinkUri` base is centralized in `DiagnosticRules.cs`; publish the `docs` directory at that configured repository URL (or update the base) before distributing the package.
