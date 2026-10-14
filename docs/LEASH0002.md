# LEASH0002: Too many parameters

```text
LEASH0002: 'Foo' has 8 parameters (limit 6).
```

Count each explicitly written parameter once, including `out`, `ref`, `in`, `params`, optional parameters, and `this` extension receivers. A tuple is one parameter. Bodyless declarations, delegates, indexers, local functions, lambdas, primary constructors, and positional records are included. Implicit parameters are not inferred; partial declarations are checked separately.

The default limit is 6. Equality is allowed. Set `leash.max_parameters` to an integer from 0 to 2,147,483,647. This check is independent of LEASH0001 and LEASH0003.

Review responsibilities. Group values only when they represent one coherent concept. External contracts can use a [reasoned exemption](LEASH0005.md); they are not automatically excluded.
