# Workspace data isolation

Every authenticated customer request must carry a verified `tid` and `oid` claim. The server resolves the active workspace by joining the verified tenant and object IDs to a stored workspace membership; a client-supplied tenant ID never selects the active workspace.

Persistence code treats `WorkspaceId` as a mandatory scope. `WorkspaceRepository` is constructed with one workspace scope and rejects reads, updates, membership writes, and deletes for any other workspace ID. The database additionally enforces unique tenant and membership keys.

The platform stores only metadata: workspace identity and status, membership metadata, connection status and approved scope JSON, UI settings JSON, and invitation metadata including a nonce hash. It does not store access tokens, refresh tokens, client secrets, passwords, or other credentials.

Platform administrators are limited to the configured allowlist of Atea object IDs and may provision workspaces and change platform membership metadata. This role is separate from Microsoft Graph capabilities and never grants Microsoft 365 directory authority. Directory operations require the later Graph capability boundary and customer-granted permissions.

EF migrations run automatically only in Development. Production startup never mutates the database; production migrations are a deployment responsibility.
