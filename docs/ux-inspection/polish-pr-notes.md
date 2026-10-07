# UX polish PR notes

## Baseline (before changes)

- `npm run build` (src/Web): pass
- `npm test` (src/Web): 0 tests, pass
- `npm run test:behavior` (src/Web): 44 files / 331 tests pass
- `tests/Web.UnitTests` `npm test`: 4 pass
- `tests/Web.E2E` `npm test`: 11 files / 28 tests pass
- `dotnet` is not installed locally; API tests are CI-only.

## Contrast measurements

Measured by `tests/Web.UnitTests/contrast.test.mjs` (WCAG ratios):

| Theme | Tone | Text on bg | Border on surface |
|---|---|---|---|
| light | success | 8.30 | 3.30 |
| light | warning | 8.15 | 5.02 |
| light | danger | 6.80 | 4.83 |
| light | info | 8.49 | 5.17 |
| dark | success | 10.96 | 6.95 |
| dark | warning | 11.00 | 7.38 |
| dark | danger | 8.99 | 4.21 |
| dark | info | 8.86 | 4.31 |

Muted on surface: 7.40 light / 11.63 dark. Primary on surface: 4.53 light / 4.74 dark.

## How to test manually

## Traceability
