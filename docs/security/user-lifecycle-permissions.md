# User Lifecycle Permissions

Task 10 adds the safe mutation foundation for user lifecycle administration. The API remains the enforcement point for every action; the React dialogs are review and confirmation aids only.

## Authorization Model

Each mutation resolves the active workspace from verified token claims and server-side membership. The request never supplies a tenant or workspace authorization input. Before Graph is called, the API evaluates the signed-in actor's delegated Microsoft Graph scopes, active tenant-wide Entra role assignments, PIM state, administrative-unit scope, and workspace membership.

Directory-synchronized or otherwise external-source users are read-only for Task 10 mutations. The API returns `source_of_authority_read_only` before Graph is called.

## Operation Matrix

| Action | API route | Required capability | Delegated Graph scopes | Tenant role expectation |
| --- | --- | --- | --- | --- |
| Create cloud user | `POST /api/users` | `users.create` | `Directory.Read.All`, `User.Read.All`, `User.Create` | User Administrator or Global Administrator, active and tenant-wide |
| Edit approved profile fields | `PATCH /api/users/{id}` | `users.update` | `Directory.Read.All`, `User.Read.All`, `User.ReadWrite.All` | User Administrator or Global Administrator, active and tenant-wide |
| Disable sign-in | `POST /api/users/{id}/disable` | `users.disable` | `Directory.Read.All`, `User.Read.All`, `User.EnableDisableAccount.All` | User Administrator or Global Administrator, active and tenant-wide |
| Reactivate sign-in | `POST /api/users/{id}/reactivate` | `users.disable` | `Directory.Read.All`, `User.Read.All`, `User.EnableDisableAccount.All` | User Administrator or Global Administrator, active and tenant-wide |
| Add or remove group membership | `POST` or `DELETE /api/users/{id}/groups/{groupId}` | `groups.manage_members` | `Directory.Read.All`, `Group.Read.All`, `GroupMember.ReadWrite.All` | Groups Administrator or Global Administrator, active and tenant-wide |
| Assign or remove license | `POST` or `DELETE /api/users/{id}/licenses/{skuId}` | `licenses.assign` | `Directory.Read.All`, `User.Read.All`, `LicenseAssignment.ReadWrite.All` | License Administrator or Global Administrator, active and tenant-wide |

Eligible inactive roles produce PIM-required capability states only. Task 10 does not activate PIM; Task 11 owns activation.

## Idempotency

Every mutation requires `Idempotency-Key`. The key is scoped to workspace, actor object ID, operation, target ID, and key value. Exact replay returns the stored safe result and does not call Graph again. Reusing the same scoped key with a changed payload returns `409 idempotency_key_reused`.

The idempotency record stores only a request fingerprint, result category, HTTP status, safe result JSON, Graph correlation ID, Graph request ID, actor, workspace, operation, target, key, and timestamp. It does not store request bodies, access tokens, refresh tokens, temporary passwords, raw Graph responses, or authorization headers.

## Temporary Password Handling

Create-user generates a temporary password in memory immediately before the Graph request. The Graph request sets `forceChangePasswordNextSignIn=true`. The plaintext password is returned only on the initial successful create response as `temporaryCredentialNotice`; it is not persisted in idempotency, audit, logs, or database rows. Replays return the safe result without the plaintext password.

## Audit Hooks

Task 10 introduces `IAuditWriter` and invokes it for mutation outcomes with workspace ID, actor object ID, action, target, result, timestamp, safe failure category, and Graph correlation metadata. The default writer is a no-op. Task 12 will add the persisted audit writer and audit schema.
