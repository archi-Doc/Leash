# LEASH0003: Too many out parameters

```text
LEASH0003: 'Foo' has 2 out parameters (limit 1).
```

Count parameters with an explicit `out` modifier in the parameter lists covered by [LEASH0002](LEASH0002.md). An `out` parameter also counts toward the total parameter limit. Report both rules if both limits are exceeded.

The default limit is 1. Equality is allowed. Set `leash.max_out_parameters` to an integer from 0 to 2,147,483,647. The analyzer does not infer writes through `ref` or `out` call arguments.

Review the result contract. Consider returning related results together when they represent one coherent result.
