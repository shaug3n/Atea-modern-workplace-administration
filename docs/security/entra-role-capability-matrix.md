# Entra Role Capability Matrix

Catalog version: `2026-09-20`

Task 6 keeps Microsoft 365 authorization Graph-authoritative. The API evaluates a workspace-scoped snapshot for UI and pre-flight guard decisions, but Graph mutation adapters remain the authority for the final write result.

## Stable Role Templates

| Role | Template ID | Capability use |
| --- | --- | --- |
| Global Administrator | `62e90394-69f5-4237-9190-012177145e10` | All Microsoft 365 capabilities when required delegated scopes are present |
| Global Reader | `f2ef992c-3afb-46b9-b7cf-a126ee74c451` | `users.view`; mutation capabilities become `read_only` |
| User Administrator | `fe930be7-5e62-47db-91af-98c3a49a38b1` | User lifecycle capabilities |
| Groups Administrator | `fdd7a751-b60b-444a-984c-02652fe8fa1c` | Group membership management |
| License Administrator | `4d6ac14f-3453-41d0-bef9-a3e0c569773a` | License assignment |
| Privileged Role Administrator | `e8611ab8-c189-46e8-94e1-60213ab1f814` | Role and PIM assignment |

Display names are presentation data only. Capability evaluation keys only on stable role template IDs returned from Microsoft Graph role definitions.

## Capability Matrix

| Capability | Required delegated scopes | Required active role template IDs | Non-active/failed state |
| --- | --- | --- | --- |
| `users.view` | `Directory.Read.All` or `User.Read.All` | Global Administrator, Global Reader, User Administrator | `hidden` |
| `users.create` | read scope plus `User.Create` | Global Administrator or User Administrator | `read_only`, `consent_required`, or PIM state |
| `users.update` | read scope plus `User.ReadWrite.All` | Global Administrator or User Administrator | `read_only`, `consent_required`, or PIM state |
| `users.disable` | read scope plus `User.EnableDisableAccount.All` and `User.Read.All` | Global Administrator or User Administrator | `read_only`, `consent_required`, or PIM state |
| `users.reset_password` | read scope plus `User-PasswordProfile.ReadWrite.All` | Global Administrator or User Administrator | `read_only`, `consent_required`, or PIM state |
| `groups.manage_members` | `Directory.Read.All` or `Group.Read.All`, plus `GroupMember.ReadWrite.All` | Global Administrator or Groups Administrator | `hidden`, `consent_required`, or PIM state |
| `licenses.assign` | read scope plus `LicenseAssignment.ReadWrite.All` | Global Administrator or License Administrator | `hidden`, `consent_required`, or PIM state |
| `roles.assign` | `Directory.Read.All` plus `RoleManagement.ReadWrite.Directory` | Global Administrator or Privileged Role Administrator | `hidden`, `consent_required`, or PIM state |
| `workspace.settings.manage` | Platform-only; no Graph scope | Workspace platform role `admin`/`owner` or Atea operator | `hidden` |

## PIM State Mapping

| Graph/PIM signal | Capability state |
| --- | --- |
| Eligible but inactive | `pim_activation_required` |
| Approval required or pending approval | `pim_approval_required` |
| MFA required for activation | `pim_mfa_required` |
| Eligibility expired | `pim_eligibility_expired` |
| Unknown PIM status | `temporarily_unavailable` |

## Test Tenant Assignments

The Task 6 test fixtures model the minimum validation set:

| Fixture | Assignments | Expected result |
| --- | --- | --- |
| Global Reader | Active Global Reader with read scopes | `users.view=allowed`; user mutations `read_only` |
| User Administrator | Active User Administrator with user lifecycle scopes | user lifecycle capabilities `allowed` |
| Missing read permission | User Administrator without directory read scope | user sections `hidden` |
| Consent missing | Graph snapshot unavailable with `consent_required` | Microsoft 365 capabilities `consent_required` |
| PIM eligible | Global Reader active plus eligible User Administrator | user mutation `pim_activation_required` |
| PIM approval/MFA/expired | Eligible User Administrator with each PIM signal | exact PIM capability state |
| Graph unavailable | Snapshot read failure | Microsoft 365 mutations fail closed as `temporarily_unavailable` |

No test fixture contains raw Graph payloads, bearer tokens, or Authorization headers.
