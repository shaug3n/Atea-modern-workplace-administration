# Local user and device management demonstration

This is a local, non-production demonstration path for the user-security and
managed-device features. Use a dedicated Microsoft Entra test tenant and
disposable test objects only. The browser talks to this API; it must never call
Microsoft Graph directly.

## Start the local stack

Prerequisites are Docker, the .NET 9 SDK, Node.js/npm, and a dedicated test
tenant if the real-tenant checks below are being performed. From the repository
root, copy the local example configuration and keep the copy untracked:

```bash
cp .env.example .env
```

The example contains local-only placeholder values. Replace the Entra client
values with the dedicated test app registration values in your local `.env`
only. Never commit `.env`, client secrets, access tokens, invitation nonces,
TAP codes, or raw Graph responses.

The reproducible all-in-Compose startup is:

```bash
docker compose config --quiet
docker compose up --build
```

For the split local-development path, use three terminals from the repository
root:

```bash
# Terminal 1
set -a; . ./.env; set +a
docker compose up -d postgres
docker compose ps postgres
```

```bash
# Terminal 2
set -a; . ./.env; set +a
dotnet run --project src/Api/Atea.UnifiedWorkplace.Api.csproj --urls http://localhost:8080
```

```bash
# Terminal 3
set -a; . ./.env; set +a
cd src/Web
npm ci
npm run dev -- --host 0.0.0.0 --port 5173
```

The API health endpoint is anonymous and should return `{"status":"ok"}`:

```bash
curl --fail http://localhost:8080/health
```

The authenticated API ping is a bearer-protected smoke check. Without a
customer bearer token it must return structured `401` rather than `200`:

```bash
curl -i http://localhost:8080/api/ping
```

Open `http://localhost:5173`. The local `/admin` provider is only for platform
workspace/invitation setup; it is not customer authentication and does not
grant Graph permissions. Use the local values from `.env.example` only on a
Development API, then hand the one-time invitation instruction to the
dedicated test-tenant administrator without storing it.

## Configure delegated Entra consent

On the API app registration, add these two Microsoft Graph **delegated** API
permissions in addition to the existing permissions listed in
`docs/security/entra-app-registration.md`:

1. `User.RevokeSessions.All`
2. `DeviceManagementManagedDevices.PrivilegedOperations.All`

The second permission is required for every device command, including Sync and
Remote lock. `DeviceManagementManagedDevices.ReadWrite.All` alone is not
sufficient. Do not add Graph permissions to the SPA registration.

After both permissions are added, a tenant administrator must select **Grant
admin consent** for the dedicated test tenant. Sign out of the SPA, clear the
localhost site session or use a private window, restart the API, and sign in
again so the delegated OBO token is reacquired. A configured permission list
is not proof that the active delegated token contains the permission.

## Safe tenant demonstration

Use only a dedicated, non-production tenant and record only object IDs, scope
names, capability states, and safe correlation/request IDs.

1. **Global Reader/read-only check.** Sign in as a user with an active Global
   Reader role. Confirm that user details, associated devices (when the read
   permission is consented), roles/PIM state, and device metadata are readable.
   Confirm that mutation controls are hidden or marked read-only. Do not use a
   stale SPA control as evidence of authorization; the API must deny mutations.
2. **Authorized/PIM check.** Use an approved test account with the required
   active role and delegated consent. For an eligible but inactive PIM role,
   follow the displayed Entra activation link interactively, complete any
   required MFA, approval, justification, or duration steps, then refresh the
   capability state. The application must show an activation/handoff state
   until Entra reports the role active; never bypass or silently activate PIM.
3. **Disposable-user TAP check.** Create or select a disposable test user and
   open its security methods. Issue the fixed local-MVP Temporary Access Pass:
   single-use, immediately available, valid for 60 minutes. Copy it only into
   the approved test sign-in flow. The TAP code is shown exactly once and is
   intentionally unrecoverable once the dialog is closed; closing the dialog,
   refreshing, replaying the request, or losing the clipboard cannot reveal it
   again. Never place the code in a ticket, screenshot, log, audit record, or
   test output.
4. **Authentication-method reset/remove.** On the same disposable user, use
   reset all removable MFA methods, then remove one individual non-password
   method. Verify that password authentication remains non-removable and that a
   TAP method can be removed without ever displaying its secret again.
5. **Associated-device check.** Open the disposable user's details and verify
   that the Associated devices section calls the user-scoped route and links to
   `/devices?device=<device-object-id>`. If the tenant rejects the targeted
   association query, verify the truthful unavailable state; do not accept an
   empty list as proof that there are no devices and do not perform an
   inventory-wide fallback scan.
6. **Safe Sync check.** Select a disposable managed test device, open its
   Actions menu, choose **Sync device**, review the confirmation, and submit it
   once with a fresh idempotency key. Verify the safe queued/succeeded result and
   the audit outcome. This is the only device mutation in this demonstration.

## Destructive-action prohibition

**DO NOT RETIRE OR WIPE AN EVERYDAY, SHARED, OR NON-DISPOSABLE DEVICE.** The
local E2E and Docker contract tests never submit Retire or Wipe. Those actions
require typed confirmation and are out of this runbook. If a separately
approved experiment ever needs one, use only a disposable device whose owner
has approved the irreversible data-loss and enrollment consequences, and keep
that experiment outside automated tests.

## Validation and cleanup

Run the repository checks from the repository root or their stated working
directories:

```bash
dotnet test tests/Api.UnitTests/Api.UnitTests.csproj
dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj
```

```bash
cd src/Web
npm run test:behavior
npm run build
```

```bash
cd tests/Web.E2E
npm test
```

After the demonstration, deactivate temporary PIM assignments, remove the
disposable user/device test data through the approved tenant process, and
clear any local TAP or token material. Stop the stack with:

```bash
docker compose down
```

Use `docker compose down -v` only when intentionally discarding the local
PostgreSQL volume.
