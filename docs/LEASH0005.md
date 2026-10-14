# LEASH0005: Invalid exemption

Use the attribute that the package generates in the `Leash` namespace, or a project-defined attribute with the same name, with a non-blank string literal reason:

```csharp
using Leash;

[LeashExempt("External contract requires this signature.")]
public void Execute(int a, int b, int c, int d, int e, int f, int g) { }
```

Match the final written identifier `LeashExempt` or `LeashExemptAttribute`. Qualified and generic names are matched by that identifier; aliases are not resolved. Require exactly one positional ordinary, verbatim, or raw string literal. Constants, interpolations, concatenations, named arguments, UTF-8 literals, and empty or whitespace-only reasons are invalid.

Functions (including supported attributed lambdas and individual accessors), fields, properties, indexers, and delegates can be exempted. A field exemption covers its declarators. A property or indexer exemption covers its accessors and initializer. A `method:` attribute on a type with a primary constructor applies only to that constructor. Whole types, assemblies, modules, parameters, and return values cannot be exempted.

An exemption skips only LEASH0001–LEASH0003 for its associated targets. It does not change measured counts, remove a nested function's contribution to its parent, or flow into nested functions. All recognized attributes on one declaration must be valid. Attribute validation and configuration errors remain active.

Correct the attribute target or reason. If `leash.allow_exempt = false`, remove the attribute or change that setting through the project's review process. Invalid or disabled attributes produce one error each and grant no exemption.
