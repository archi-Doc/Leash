# Leash Specification

## 1. Purpose and scope

Leash is a syntax-based C# analyzer that limits implementation size and parameter counts. It discourages dense or unnatural code written to bypass limits, including AI-generated code.

The analyzer uses the Roslyn syntax tree only. It performs no type analysis, symbol resolution, or control-flow analysis. It does not limit physical lines or characters per line; formatting belongs to the formatter. Changes to whitespace, line breaks, or comments must not affect measurements when the parsed code remains the same.

Limits are inclusive: a value equal to its limit is allowed. Violations are build errors when the analyzer runs. These rules do not establish design quality. Meaningless method extraction and artificial parameter containers still require review. Version 1 does not limit nesting depth, type size, file size, or the number of `ref` parameters.

## 2. Rules and configuration

### 2.1 Rules

| ID | Condition | Default limit |
| --- | --- | ---: |
| LEASH0001 | Logical lines exceed the size limit | 50 |
| LEASH0002 | Explicit parameters exceed the total parameter limit | 6 |
| LEASH0003 | Explicit `out` parameters exceed the `out` parameter limit | 1 |
| LEASH0004 | A recognized configuration value is invalid | — |
| LEASH0005 | An exemption is invalid or disabled | — |

All five diagnostics are enabled by default, have severity `Error`, and carry `WellKnownDiagnosticTags.NotConfigurable`. Parameter and size checks are independent. An `out` parameter counts toward both parameter limits; report both diagnostics if both limits are exceeded.

### 2.2 Configuration

Read effective options once per syntax tree through Roslyn's analyzer configuration, following standard `.editorconfig` inheritance and overrides.

```ini
[*.cs]
leash.max_lines = 50
leash.max_parameters = 6
leash.max_out_parameters = 1
leash.allow_exempt = true
```

| Option | Allowed value | Default |
| --- | --- | --- |
| `leash.max_lines` | Integer from 1 to 2,147,483,647 | `50` |
| `leash.max_parameters` | Integer from 0 to 2,147,483,647 | `6` |
| `leash.max_out_parameters` | Integer from 0 to 2,147,483,647 | `1` |
| `leash.allow_exempt` | `true` or `false` | `true` |

Trim surrounding whitespace. Integers use decimal digits only, without signs or separators. Boolean values are case-insensitive. Use defaults only for missing effective values; ignore unknown keys.

For each invalid recognized option, report LEASH0004 once per affected, non-generated syntax tree. Do not silently substitute a default. Skip only the rule that depends on an invalid numeric option and continue other checks. If `allow_exempt` is invalid, grant no exemptions and continue limit checks; attribute format errors can still produce LEASH0005. Configuration errors cannot be exempted.

Analyzer activation, configuration changes, and generated-code settings remain subject to build policy and review.

## 3. Logical lines

### 3.1 Terms and formula

**An element is a single syntax token: an identifier, keyword, operator, punctuation, or literal.** Use *elements* consistently in diagnostics, the README, and diagnostic help pages.

The **statement count** is the weighted measure defined below. The diagnostic label `Statements` refers to this measure, not the raw number of C# statement nodes. It accounts for declarations, assignments, increments, and control structures.

For each measurement unit:

```text
S = statement count
E = element count
logical lines = max(S, ceiling(E / 10))
```

The conversion factor is fixed at **10** for version 1 and is not configurable. Round up once per unit, after all contributions have been combined. Empty units have zero logical lines. With `S = 42` and `E = 573`, the result is `max(42, 58) = 58`.

### 3.2 Statement count

For a statement or control header, let `D`, `A`, and `I` be its directly owned declaration targets, assignment targets, and increment/decrement targets. Each target contributes one. Direct ownership excludes child statements and nested function bodies; count those separately and add their contributions.

| Construct | Contribution to `S` |
| --- | --- |
| Ordinary statement | `max(1, D + A + I)` |
| Control structure | `1 + D + A + I` for its header, plus its child contributions |
| Each `switch` statement section | 1, plus its label-expression targets and contained statements |
| Local function declaration | 1, plus its body |
| Lambda or anonymous-method wrapper | 0, plus its body |
| Block, empty statement, or ordinary statement label | 0, plus any contained statement |
| Whitespace or comment | 0 |

Control structures are `if`, `for`, `foreach`, `while`, `do`, `switch`, `try`, each `catch`, `finally`, `using` statements, `lock`, `fixed`, and `checked`, `unchecked`, and `unsafe` blocks. Headers include conditions, initializers, iterators, resource declarations, and exception filters. A `do` condition belongs to its loop header. An `else` contributes zero; an `else if` contributes the nested `if`.

`using var` is an ordinary declaration statement. `return`, `throw`, `break`, `continue`, `goto`, and `yield` statements follow the ordinary rule regardless of position. Expressions such as conditional expressions, calls, `switch` expressions, and query expressions have no extra statement contribution of their own; their targets and elements still count.

Apply these target rules:

- Count local declarations, deconstruction declarations, `out var`, pattern variables, and `foreach` and `catch` variables. Do not count parameters as declaration targets.
- Count each leaf of a deconstruction declaration or assignment. Count `_` in a target position as one without resolving whether it is a discard or an identifier.
- Count a declaration target once; its initializer's `=` is not another assignment. An assignment inside that initializer still counts.
- Count assignments at any expression depth, including compound and chained assignments and assignments in object, indexer, and `with` initializers. A member or indexer assignment has one target; assignments inside its receiver or index arguments count separately.
- Count each prefix or postfix `++` or `--` target. Do not infer writes through calls: an `out` or `ref` argument without a declaration is not an assignment target.

For a `switch` statement section, combine all targets in its case patterns and `when` clauses, then add them to the section's contribution of one. Multiple labels do not add multiple section contributions. Ordinary labels contribute zero. Never count a target in both a parent and a child contribution.

### 3.3 Expression bodies and initializers

Treat an expression body as one synthetic ordinary statement: `max(1, D + A + I)`, plus any nested function bodies. This applies equally to expression-bodied members, local functions, and lambdas.

Treat each field or property initializer the same way. The declared member itself is not a declaration target in this unit. For example, `int value = Compute();` has an initializer statement count of one. Multiple field declarators have separate initializer units.

Explicit constructor initializers also use this rule; their ownership is defined in section 4.2.

### 3.4 Element count

Count each non-missing Roslyn token in the unit's measurement range once. Do not retokenize text or split a token based on its character length.

- Exclude trivia: whitespace, line breaks, comments, directives, disabled code, and skipped tokens stored as trivia. Exclude the end-of-file token.
- Do not parse literal contents as code. For interpolated strings, use Roslyn's tokens, including tokens in interpolation expressions. A long literal does not gain elements merely because it contains more characters.
- For a block body, count everything inside its outer braces, excluding those two braces. For an expression body, count the expression, excluding its outer `=>` and trailing `;`.
- For a member initializer, count only the initializer expression, excluding the member declaration, its initializer `=`, and its trailing separator or semicolon.
- Exclude the unit's own signature, attributes, parameter list, and parameter defaults. Internal parentheses, braces, separators, and semicolons within the measurement range count.
- A nested function's entire syntax within a parent's measurement range counts toward the parent, including its signature, attributes, parameter defaults, and wrapper. These parts remain excluded from the nested function's own body measurement.

For example, `x = a + 1;` contains six elements: `x`, `=`, `a`, `+`, `1`, and `;`.

## 4. Measurement units and ownership

### 4.1 Size checks

Apply LEASH0001 to:

| Unit | Measurement range |
| --- | --- |
| Method, constructor, static constructor, or finalizer | Its body, plus any explicit constructor initializer |
| Operator, conversion operator, or accessor | Its body |
| Expression-bodied property or indexer | Its expression body, counted once |
| Local function, lambda, or anonymous method | Its body |
| Top-level code | All top-level statements in one source file, as one unit |
| Field or property initializer | Each initializer expression separately |
| Primary constructor with an explicit base call | That base call |

Default interface implementations follow the same rules. An expression-bodied property or indexer does not also create an implicit getter unit. Accessors with bodies, including event accessors, are separate units. Auto-accessors and other declarations without a body or explicit constructor initializer have no size check.

Do not synthesize units for compiler-generated members, implicit base calls, or implicit execution. Field and property initializers are independent of constructors and are not added to their totals. Member signatures, attributes, and parameter defaults are outside size analysis, except where they appear inside another unit's range as described in section 3.4.

### 4.2 Constructor initializers

For `: base(...)` and `: this(...)`, add the synthetic-statement contribution defined in section 3.3 and the call's elements to the constructor body totals before rounding. Count the `base` or `this` keyword, parentheses, arguments, and separators; exclude the preceding colon.

A primary constructor has no explicit body. Its explicit base call therefore forms its size unit. Count the written base type and argument list, including generic type arguments when present, but exclude the preceding colon and any other base types or interfaces. Do not invent a `base` token. Its member initializers remain separate units. Parameter checks still apply even when it has no explicit base call.

### 4.3 Nested functions

Check each nested function independently and include its contributions in the nearest enclosing measurement unit. Inclusion is transitive through all enclosing units. A function with no enclosing unit is checked on its own.

Add raw statement contributions before calculating logical lines. Count each element once within each containing unit; do not add a child's elements again if the parent's traversal already included them. Parent totals include nested signatures and wrappers, so they cannot be calculated from child body totals alone.

An exemption never changes measurement. Exempting a child does not reduce its parent's totals, and exempting a parent does not exempt a child's independent checks. Moving code into a nested function alone therefore cannot reduce the parent's size.

## 5. Parameter checks

Apply LEASH0002 and LEASH0003 to every explicit parameter list on a callable declaration or expression, even without a body. This includes methods, constructors, operators, local functions, lambdas, anonymous methods, delegates, indexers, primary constructors, and positional records. A single unparenthesized lambda parameter counts as one.

Each parameter counts once, including optional parameters, `params`, extension receivers marked `this`, and `ref`, `in`, or `out` parameters. A tuple parameter counts as one; do not expand its type. Only an explicit `out` modifier contributes to LEASH0003.

Do not infer omitted anonymous-method parameters, implicit accessor parameters such as `value`, or compiler-generated parameters. Function-pointer types and generic type parameter lists are not callable declarations and do not create parameter checks. The receiver specification of a C# 14 extension block is not a parameter list of a callable declaration either. Check each `partial` declaration separately, without matching declarations or merging diagnostics.

Do not automatically exclude overrides, interface implementations, or interop declarations. Use a reasoned exemption when an external contract requires an exception.

## 6. Generated code and exemptions

### 6.1 Generated code

Use `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` and Roslyn's generated-code classification. Add no custom inference based on path shape or file existence. Roslyn disables generated-code callbacks and diagnostic reporting under this setting; its classification is not proof of origin. See the [Roslyn API contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.generatedcodeanalysisflags).

Generated-code declarations and configuration are subject to review.

### 6.2 Reasoned exemptions

The only supported code-level exemption is a reasoned attribute:

```csharp
[LeashExempt("External contract requires this signature.")]
public void Execute(int a, int b, int c, int d, int e, int f, int g)
{
}
```

The package's source generator adds the attribute type `Leash.LeashExemptAttribute` to each consuming compilation. It is internal, carries `Microsoft.CodeAnalysis.EmbeddedAttribute` so that copies in different assemblies do not conflict, is conditional so that its usages are not emitted to metadata, and permits methods, constructors, properties, fields, events, and delegates as C# attribute targets. A project can define its own attribute with a recognized name instead. In either case the analyzer matches the written name, not the resolved type, and the generated source is generated code that is not analyzed:

- Accept a final identifier of `LeashExempt` or `LeashExemptAttribute`, including qualified and generic names. Do not resolve aliases. A different alias name is not recognized; a matching written name is recognized regardless of what it resolves to.
- Require exactly one positional argument: a string literal whose decoded value is not empty or whitespace-only. Ordinary, verbatim, and raw string literals are accepted. Interpolated strings, UTF-8 byte-string literals, constant names, concatenations, and named arguments are not accepted.
- Apply the exemption to the associated declared function, field, property, indexer, or delegate. A field declaration applies to all its declarators. A property or indexer exemption also covers its accessors and initializer, where present.
- A `method:` attribute on a type with an explicit primary constructor applies only to that constructor. This is an attribute on the constructor, not a type-wide exemption; see [C# primary constructor attributes](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/instance-constructors#primary-constructors).
- Do not allow exemptions on whole types, assemblies, modules, parameters, or return values. An attribute cannot exempt a unit where C# provides no legal attribute placement, such as top-level statements or an anonymous method. An attributed lambda can be exempted when the selected C# version permits it.

A valid exemption skips only LEASH0001–LEASH0003 for its associated targets. It does not skip attribute validation or change nested-function ownership. Recognized attributes on unsupported targets or with invalid arguments produce LEASH0005 and grant no exemption. If multiple recognized attributes occur on one target, all must be valid for an exemption to apply.

When `allow_exempt = false`, every recognized exemption attribute produces LEASH0005 and grants no exemption. Report at most one LEASH0005 per invalid or disabled attribute. Unrecognized attribute names have no special meaning.

### 6.3 Enforcement boundary

Use `NotConfigurable` on all five diagnostics to prevent severity changes and ordinary suppression, including configuration-based suppression and source pragmas. This is the [Roslyn diagnostic contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.wellknowndiagnostictags.notconfigurable). A default error diagnostic also cannot be suppressed by a [Roslyn diagnostic suppressor](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.diagnosticsuppressor).

Verify suppression behavior in every supported host. Enforcement requires the analyzer to be installed, enabled, and executed. Leash cannot prevent its removal, disabled analysis, altered configuration, or host behavior outside that contract.

Host verification found that Roslyn 4.14 still applies `SuppressMessage` and `UnconditionalSuppressMessage` attributes to `NotConfigurable` analyzer errors after they are reported. This is a host limitation: these attributes must be prohibited by build policy and review for Leash IDs. The analyzer does not resolve suppression attributes semantically or falsely mark its diagnostics as compiler diagnostics to bypass the host. Ordinary severity overrides, `NoWarn`, source pragmas, and diagnostic suppressors remain blocked. See Roslyn's [source suppression implementation](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/DiagnosticAnalyzer/SuppressMessageAttributeState.cs).

## 7. Diagnostic contract and help

### 7.1 Messages

Use these English message formats. Braces below denote substituted values; the diagnostic ID is supplied by the host and is not part of `messageFormat`.

```text
LEASH0001: '{name}' is {lines} logical lines long (limit {limit}). Statements: {statements}, elements: {elements}.
LEASH0002: '{name}' has {parameters} parameters (limit {limit}).
LEASH0003: '{name}' has {outParameters} out parameters (limit {limit}).
LEASH0004: Invalid value '{value}' for '{option}'. Expected {allowedValues}.
LEASH0005: Invalid exemption on '{name}': {reason}.
```

In particular, a unit named `Foo` with 42 statements and 573 elements under the default limit must produce:

```text
LEASH0001: 'Foo' is 58 logical lines long (limit 50). Statements: 42, elements: 573.
```

Use decimal integers without grouping separators. Escape embedded line breaks in configuration values so diagnostics remain single-line. LEASH0004 must state the allowed range or Boolean values. LEASH0005 must identify the invalid target, required argument format, or disabled setting and explain the required correction.

### 7.2 Names and locations

Use source-based display names without symbol resolution. Named declarations use their declared name; constructors use the type name, finalizers use `~Type`, accessors use names such as `Property.get` or `this.set`, and operators use their written operator form. Initializers use names such as `field initializer` or `Property initializer`. Anonymous units use `lambda`, `anonymous method`, or `top-level statements`.

Attach each diagnostic to the smallest useful source span: a declaration name, accessor keyword, operator token, lambda arrow, anonymous-method `delegate` keyword, or the first top-level statement token. Parameter diagnostics use the same declaration location. LEASH0005 points to the attribute. Source locations distinguish overloads and repeated anonymous names.

For LEASH0004, attach the diagnostic to the beginning of the affected source file; effective analyzer options do not necessarily expose the originating `.editorconfig` entry's span. Include the option and invalid value in the message.

### 7.3 Diagnostic descriptions and documentation

Provide a stable help page for each diagnostic through `helpLinkUri`. The LEASH0001 description and help page must include the element definition from section 3.1, explain the weighted statement count, and show the formula and boundary rules. Any README explanation of the metric must use the same terminology and definition.

Keep remediation guidance in descriptions and help pages so the displayed messages remain concise:

| Rule | Guidance |
| --- | --- |
| LEASH0001 | Separate responsibilities and remove duplication. Avoid meaningless extraction. |
| LEASH0002 | Review responsibilities. Group values only when they represent one coherent concept. |
| LEASH0003 | Review the result contract. Consider returning related results together. |
| LEASH0004 | Correct the option using the allowed values. |
| LEASH0005 | Correct the attribute target or reason, or remove a disabled exemption. |

Do not offer automatic fixes that split methods or pack parameters into artificial types.

## 8. Examples

### 8.1 Statement contributions

These are independent fragments; comments show statement counts before the element-based minimum is applied.

```csharp
Run();                         // 1
Prepare(); Run(); Finish();     // 3

int x = 0;                     // 1
int p = 1, q = 2;               // 2
x = y = 0;                     // 2
var (a, b) = GetPair();         // 2
(a, _) = GetPair();             // 2
var c = new C { A = 1 };        // 2: declaration + member assignment
return x = Calculate();        // 1: max(1, one assignment)

if (ok) { A(); B(); }           // 3: if + two statements
for (int i = 0; i < 3; i++)     // 3: loop + declaration + increment
{
    Run();                     // 1; entire loop = 4
}
```

The bodies of `x => F(x)` and `x => { return F(x); }` both have `S = 1`. Their element counts are 4 and 6 respectively. Equivalent expression and block forms need not have identical element counts or logical lines.

### 8.2 Nested functions

```csharp
void Outer()
{
    void Inner() { Run(); }
    Inner();
}
```

`Inner` has `S = 1`, `E = 4`, and one logical line. `Outer` has `S = 3` (local declaration + nested body + call), `E = 14`, and three logical lines. Its element count includes the nested signature and braces. Exempting `Inner` does not change these parent totals.

### 8.3 Initializers and parameter limits

```csharp
class Sample
{
    private int value = Load(); // Separate initializer: S = 1, E = 3.
    private static int Load() => 0;

    public Sample() : this(0)   // Constructor: S = 1, E = 4.
    {
    }

    public Sample(int initial) { value = initial; }

    public void Read(int a, int b, int c, int d, int e,
                     out int x, out int y)
    {
        x = a;
        y = b;
    }
}
```

Under the defaults, `Read` produces both LEASH0002 (7 parameters, limit 6) and LEASH0003 (2 `out` parameters, limit 1). The empty constructor's explicit `this(0)` call counts; the field initializer is not added to either constructor.

## 9. Implementation and acceptance criteria

### 9.1 Implementation

Use a shared syntax traversal for statement and element counts. Aggregate nested units without rescanning their bodies for every ancestor. Avoid syntax stringification, reparsing, and unnecessary intermediate collections. Keep mutable counters local to each analysis operation, support concurrent analysis and cancellation, and handle deep nesting without stack exhaustion.

Use arithmetic that cannot overflow for valid syntax trees and configuration values. Calculate ceilings without overflowing an intermediate sum. Missing tokens do not count; incomplete code must not crash the analyzer. Apply the same syntax rules to the parsed portions of incomplete code without semantic recovery or invented statements.

### 9.2 Required verification

| Area | Required cases |
| --- | --- |
| Limits and diagnostics | Below, equal to, and above each limit; independent parameter errors; exact message text; element rounding at 0, 1, 10, and 11 |
| Statement counting | Declarations, chained and compound assignments, deconstruction, discards, increments, control headers, case patterns and guards, expression bodies, and all initializer forms |
| Elements | Literal and interpolated-string tokens; nested signatures; wrapper exclusions; trivia, directives, disabled code, missing tokens, and incomplete syntax |
| Ownership | All unit kinds; primary constructors; top-level statements; nested units at multiple depths; no repeated parent contributions; separate member initializers |
| Parameters and exemptions | Bodyless and partial declarations; explicit and omitted lambda/anonymous-method parameters; supported attribute placements, aliases, reason formats, disabled exemptions, and parent/child independence |
| Host behavior | Configuration inheritance and invalid values; generated code; severity overrides; `#pragma warning disable`, `SuppressMessage`, `NoWarn`, and diagnostic suppressors |
| Stability and performance | Whitespace and comment invariance; expression/block statement-count agreement; cancellation; ordinary, large, and deeply nested input; time and allocations as input size increases |

### 9.3 Release requirements

The implementation baseline is .NET SDK 10.0.100 or later in the 10.0 family for building and testing, Roslyn 4.14 or later in the analyzer host, C# 13 for the analyzer's source and analyzed syntax, and .NET Standard 2.0 for the analyzer runtime target. Tests and benchmarks target .NET 10. IDE versions require separate host verification before they can be listed as supported. These are package compatibility decisions, not additional counting rules; do not claim support for an untested host or syntax version.

Before release, validate the specified default of 50 logical lines and fixed conversion factor of 10 against representative code and deliberately dense examples. This validation does not permit an implementation to silently choose another factor. Any change to the factor or default requires an explicit specification revision and updated examples, diagnostics documentation, and tests.
