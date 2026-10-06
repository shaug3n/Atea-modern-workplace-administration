# PIM Test Matrix

Task 11 covers Microsoft Entra directory-role PIM only. Azure resource roles and PIM for groups are intentionally unsupported and must return a safe non-success status.

## Automated Coverage

| Area | Scenario | Expected result |
| --- | --- | --- |
| API unit | Active directory role | `active`; no activation handoff |
| API unit | Eligible inactive role | `eligible_inactive`; activation available |
| API unit | No current eligible role | `not_eligible`; Graph mutation is not called |
| API unit | Approval required | `approval_required`; handoff says to wait for approval |
| API unit | MFA required | `mfa_required`; handoff says to complete MFA |
| API unit | Justification required | `eligible_inactive`; handoff asks for justification before activation |
| API unit | Policy denial | `policy_blocked`; handoff points to tenant PIM policy |
| API unit | Missing consent or authorization | `not_authorized`; no false active state |
| API unit | Pending Graph request | `activation_pending`; request ID and safe Graph correlation ID are returned |
| API unit | Transient Graph failure | `temporarily_unavailable`; handoff asks the operator to refresh and retry |
| API integration | Missing `Idempotency-Key` | `400 idempotency_key_required` |
| API integration | Missing confirmation | `409 policy_blocked`; Graph mutation is not called |
| API integration | Duplicate browser submission | One Graph mutation; replayed response returns the stored pending result |
| Web unit | Dialog confirmation | Duration, justification, explicit confirmation, and idempotency key are required |
| Web unit | Guided handoff | Pending result shows next step and does not claim the role is active |
| Web E2E | Browser boundary | Browser calls only `/api/pim/activations`, never Microsoft Graph directly |

These automated API and browser cases use deterministic fixtures. The E2E
Vitest suite runs in jsdom and does not authenticate to Entra or verify live
Graph consent, role assignments, Conditional Access, MFA, or tenant PIM policy.
The real-tenant checks below are a separate manual validation surface.

## Real Test Tenant

Use a dedicated Microsoft 365 test tenant with one cloud-only operator account. Assign the operator eligible Entra directory-role PIM access for a supported role such as Privileged Role Administrator or User Administrator. The operator should also have a Global Reader active assignment so the app can read directory and capability state without granting hidden mutation authority.

Required delegated Graph scopes:

- `Directory.Read.All`
- `RoleManagement.Read.Directory`
- `RoleManagement.ReadWrite.Directory`

Policy fixtures to validate manually before release:

- Eligible inactive role with justification required.
- Eligible role requiring approval.
- Eligible role requiring MFA or Conditional Access.
- User with no eligible assignment.
- Unsupported PIM target such as group PIM or Azure resource role.
- A throttled or temporarily unavailable Graph response, using a controlled fake transport when tenant throttling is not practical.

Cleanup after each real-tenant run:

- Deactivate active PIM assignments from the Entra admin center.
- Cancel pending approval requests created by the test operator.
- Remove any temporary eligible role assignments created for the run.
- Record Graph request IDs and correlation IDs in the test log, but do not store access tokens or raw Graph payloads.
