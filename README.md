# Leash

Leash is a syntax-only Roslyn analyzer that limits the size and parameter count of C# implementations. [SPEC.md](SPEC.md) defines the rules.

```text
LEASH0001: 'Foo' is 58 logical lines long (limit 50). Statements: 42, elements: 573.
```

Size is measured in logical lines: `max(statements, ceiling(elements / 10))`.

- **Elements** are syntax tokens: identifiers, keywords, operators, punctuation, and literals.
- **Statements** is a weighted count of declarations, assignments, increments, and control structures, not a raw count of C# statement nodes.

Comments and formatting do not count. A value equal to its limit is allowed.

## Build and test

Requires .NET SDK 10.0.100 or later in the 10.0 family. `global.json` selects the Microsoft.Testing.Platform test runner.

```sh
dotnet restore Leash.slnx
dotnet build Leash.slnx -c Release --no-restore
dotnet test --solution Leash.slnx -c Release --no-build
dotnet pack Leash/Leash.csproj -c Release --no-build -o artifacts/packages
```

| Project | Purpose |
| --- | --- |
| `Leash` | Roslyn analyzer and exemption-attribute generator; `netstandard2.0`, Roslyn 4.14 |
| `xUnitTest` | Tests for counting, diagnostics, configuration, exemptions, the generator, concurrency, and hosts; .NET 10 |
| `Benchmark` | BenchmarkDotNet timing and memory measurements; .NET 10 |

Compatibility:

- The analyzer host must be Roslyn 4.14 or later.
- The analyzer is written in C# 13 and targets .NET Standard 2.0.
- The supported syntax baseline is C# 13, and tests use that language version.
- Only the .NET SDK command-line host is verified. Test an IDE before claiming support for it.

Diagnostic help links use the base URL in `DiagnosticRules.cs`. Before distributing the package, publish the `docs` directory at that URL or change the base. The package also includes the help pages.

## Use in another project

Reference the analyzer project directly:

```xml
<ItemGroup>
  <ProjectReference Include="../Leash/Leash.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Or install the locally packed NuGet package with `PrivateAssets="all"`. The package places the analyzer under `analyzers/dotnet/cs`.

Set options in `.editorconfig`. These are the defaults:

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

All diagnostics are errors tagged `NotConfigurable`. Severity overrides, `NoWarn`, `#pragma warning disable`, and diagnostic suppressors cannot turn them off; change the limits instead. An invalid option is reported as an error, not replaced with its default. Code that Roslyn classifies as generated is not analyzed.

**Known gap:** Roslyn 4.14 still honors `SuppressMessage` and `UnconditionalSuppressMessage` attributes for these diagnostics, and a syntax-only analyzer cannot block them through the supported diagnostic API. Ban these attributes for Leash IDs through build policy and code review. Tests record this behavior; see [the enforcement boundary](SPEC.md#63-enforcement-boundary).

## Reasoned exemptions

A source generator in the package adds `Leash.ExemptAttribute` to every project that uses Leash, so you do not need to define it.

```csharp
[Leash.Exempt("External contract requires this signature.")]
public void Execute(int a, int b, int c, int d, int e, int f, int g) { }
```

The unqualified `[Exempt(...)]` form is also recognized in files that import the `Leash` namespace.

- The analyzer reads only the written attribute name and the reason, which must be a non-blank string literal. It does not resolve symbols or aliases.
- Exemptions do not cross function boundaries. Exempting a nested function does not reduce its parent's size, and exempting a parent does not exempt its nested functions.
- See [LEASH0005](docs/LEASH0005.md) for supported targets.

The generated type is `internal` and marked `[Microsoft.CodeAnalysis.Embedded]`, so projects linked by `InternalsVisibleTo` do not see each other's copies. It is also `[Conditional("LEASH_ATTRIBUTES")]`, so its usages are not emitted to metadata unless that symbol is defined.

A project that defines its own `ExemptAttribute` still compiles. The generated type is visible without qualification only where the `Leash` namespace is imported, and a type in the global namespace or the file's own namespace takes precedence over an imported one. If a file imports both namespaces with `using` directives, write `Leash.Exempt`.

## Benchmarks and implementation

```sh
dotnet run -c Release --project Benchmark -- --filter "*TraverseParsedTree*" --job short
dotnet run -c Release --project Benchmark -- --filter "*AnalyzeWithRoslyn*" --job short
```

The benchmarks vary the method count (10, 100, 1,000) and the code shape (ordinary statements, dense expressions, nested local functions). `MemoryDiagnoser` reports allocations.

- `TraverseParsedTree` measures the engine alone. It excludes parsing, keeps every red node alive so that Roslyn does not re-create its weakly held statement lists between iterations, and uses limits that produce no diagnostics. It allocates nothing, because the engine reuses its stack and the tree is retained.
- `AnalyzeWithRoslyn` runs the normal analyzer-driver pipeline with the default limits.

Use `--job Dry` only to check that the benchmarks run; its timings are not reliable. See [the measured baseline](docs/BenchmarkResults.md) for timings, allocations, and measurement limits.

The engine:

- visits each syntax node and token once, and adds each completed child's measurements to its parent;
- uses a reusable per-thread explicit stack instead of recursion, holding O(depth) state;
- classifies each node with one lookup in a table indexed by syntax kind;
- folds single-token nodes, such as identifiers, literals, and designations, into their parent without a stack frame;
- checks for cancellation, and clears node references before caching the stack;
- uses 64-bit counters, and rounds logical lines without an overflowing addition.

The analyzer assembly depends only on Roslyn. The analysis context is passed by value instead of through a reporting delegate, so apart from the per-thread stack, the analyzer allocates nothing per syntax tree until it reports a diagnostic. Roslyn may still create red nodes during the traversal.
