# F6 Task 3: Build metadata report

## Result

Implemented truthful web/API build metadata and the protected API System versions endpoint. Local or blank build inputs resolve to `Unavailable`; deployed build values share one CI-supplied version, commit, and branch source.

## Files changed

- `.github/workflows/validate-and-deploy.yml` — derives product version from `src/Web/package.json` and passes the version, `GITHUB_SHA`, and `GITHUB_REF_NAME` as build arguments.
- `Dockerfile` — forwards those values to the Web build stage and final API image without changing deployment policy.
- `src/Api/Program.cs` — adds the feature registration and endpoint mapping.
- `src/Api/Features/PlatformAbout/PlatformBuildMetadata.cs` — API metadata record.
- `src/Api/Features/PlatformAbout/PlatformAboutFeatureServiceCollectionExtensions.cs` — registers values from `AteaBuild__*`, normalizing missing/blank values.
- `src/Api/Features/PlatformAbout/PlatformAboutEndpoints.cs` — maps the authenticated, About-module- and About-capability-protected route.
- `src/Web/src/features/about/buildMetadata.ts` — exports `WebBuildMetadata` and the pure Vite environment reader.
- `src/Web/src/vite-env.d.ts` — declares the three optional build inputs.
- `tests/Api.IntegrationTests/PlatformAbout/PlatformAboutEndpointTests.cs` — authorization, exact API metadata, and unavailable-value integration coverage.
- `tests/Web.UnitTests/features/about/buildMetadata.test.tsx` — exact-value preservation, per-field missing/blank fallbacks, and build-environment-only coverage.

## TDD evidence

Tests were added before production implementation.

- **RED, API:** `DOTNET_ROOT="$PWD/.superpowers/sdd/plan/dotnet" PATH="$PWD/.superpowers/sdd/plan/dotnet:$PATH" dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter "FullyQualifiedName~PlatformAboutEndpointTests"` — 3 failed against the unmapped route (authorized requests returned 401 instead of the expected metadata/status).
- **RED, Web:** `npm run test:behavior --prefix src/Web -- --run ../tests/Web.UnitTests/features/about/buildMetadata.test.tsx` — failed to resolve the not-yet-created `buildMetadata` module.
- **GREEN, API selector:** same focused `dotnet test` command — 3 passed.
- **GREEN, Web selector:** same focused `npm run test:behavior` command — 3 passed.
- Full API integration suite — 199 passed, 1 skipped (`RealEntraValidationTests`, environment-dependent).
- Full Web behavior suite — 72 files, 464 tests passed.
- `npm run build --prefix src/Web` — passed; Vite reported the existing ineffective dynamic import and large-chunk advisories.
- `DOTNET_ROOT="$PWD/.superpowers/sdd/plan/dotnet" PATH="$PWD/.superpowers/sdd/plan/dotnet:$PATH" dotnet build src/Api/Atea.UnifiedWorkplace.Api.csproj --configuration Release` — passed with 0 warnings and 0 errors.
- `git diff --check` — passed.

## Self-review, concerns, and deviations

- Endpoint composition uses the existing `/api/about` workspace middleware path and existing module/capability filters. The API tests verify unauthenticated, missing-membership, and wrong-module requests are denied, and verify the endpoint carries `platform.about.view` metadata. The existing capability evaluator grants this platform-only capability to any active workspace membership, so a denied-capability response cannot be produced for this endpoint without changing the already-approved capability policy. To preserve the instruction that activation/assigned gates remain unchanged, the test checks the required capability metadata rather than fabricating a denial case.
- Local metadata is not derived from Git or another runtime lookup. CI build arguments are additive; no deployment policy, SDK pin, unrelated feature, or workspace gate was changed.
- No PR, push, deployment, approval, or deployment trigger was performed. CI check status was not queried from GitHub.

## Commit

- `03b0022eb154e28b508ea868da83407c30925a23` — `feat: expose truthful application build metadata`
- Includes `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`.
