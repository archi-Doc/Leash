# LEASH0004: Invalid configuration

```text
LEASH0004: Invalid value '0' for 'leash.max_lines'. Expected an integer from 1 to 2147483647.
```

Effective `.editorconfig` options follow Roslyn's inheritance and overrides. Surrounding whitespace is trimmed. Integers contain decimal digits without signs or separators. Boolean values are case-insensitive.

| Option | Allowed values | Default when missing |
| --- | --- | --- |
| `leash.max_lines` | 1–2,147,483,647 | 50 |
| `leash.max_parameters` | 0–2,147,483,647 | 6 |
| `leash.max_out_parameters` | 0–2,147,483,647 | 1 |
| `leash.allow_exempt` | `true`, `false` | `true` |

Correct the invalid option. Report one error per invalid option per affected non-generated file, at the start of that source file. An invalid numeric option skips only its dependent limit check. An invalid Boolean grants no exemptions. Other checks continue; defaults are never silently substituted for invalid values. Unknown keys are ignored.

This diagnostic cannot be exempted.
