# LEASH0001: Implementation is too large

```text
LEASH0001: 'Foo' is 58 logical lines long (limit 50). Statements: 42, elements: 573.
```

An element is a single syntax token: an identifier, keyword, operator, punctuation, or literal.

`logical lines = max(weighted statement count, ceiling(elements / 10))`. `Statements` is the weighted count defined in [the specification](../SPEC.md#32-statement-count), including declaration and assignment targets, increments, and control structures. Here, `max(42, ceiling(573 / 10)) = 58`.

The default limit is 50. Equality is allowed. Set `leash.max_lines` to an integer from 1 to 2,147,483,647. The conversion factor 10 is fixed.

Count tokens within the body; exclude its own signature, outer body braces or arrow, and trivia. Initializer units count their expression. Nested function bodies are checked independently and also contribute to their parents, including nested signatures and wrappers in the parent's element count. Round only after combining a unit's contributions.

```csharp
void Outer()
{
    void Inner() { Run(); }
    Inner();
}
```

`Inner` has 1 statement and 4 elements. `Outer` has 3 statements and 14 elements. Their logical-line counts are 1 and 3. Formatting changes do not alter these counts.

Separate responsibilities and remove duplication. Moving work into a nested function does not avoid the parent limit. Avoid meaningless extraction; no automatic code fix is offered.
