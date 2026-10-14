## Code change rules

### Protected regions

Do not modify, move, or delete code between `#region DoNotModify` and `#endregion`.
If completing a task would require changing such a block, stop and ask the user before proceeding.

## Project overview

Leash is a syntax-only Roslyn analyzer for C# implementation size and parameter limits. [README.md](README.md) covers usage; [SPEC.md](SPEC.md) contains the normative rules.

| Project | Purpose |
| --- | --- |
| `Leash` | Roslyn analyzer and exemption-attribute generator; `netstandard2.0`, Roslyn 4.14 |
| `xUnitTest` | Tests; .NET 10 |
| `Benchmark` | BenchmarkDotNet measurements; .NET 10 |

## Build and test

Requires .NET SDK 10.0.100 or later in the 10.0 SDK family.

```sh
dotnet restore Leash.slnx
dotnet build Leash.slnx -c Release --no-restore
dotnet test --solution Leash.slnx -c Release --no-build
```

