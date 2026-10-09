# Workspace data isolation

Every authenticated customer request must carry a verified `tid` and `oid` claim. The server resolves the active workspace by joining the verified tenant and object IDs to a stored workspace membership; a client-supplied tenant ID never selects the active workspace.

Persistence code treats `WorkspaceId` as a mandatory scope. `WorkspaceRepository` is constructed with one workspace scope and rejects reads, updates, membership writes, and deletes for any other workspace ID. The database additionally enforces unique tenant and membership keys.

Workspace-administration features store only metadata: workspace identity and status, membership metadata, connection status and approved scope JSON, UI settings JSON, and invitation metadata including a nonce hash. They do not store access tokens, refresh tokens, client secrets, passwords, or other credentials.

Platform administrators are limited to the configured allowlist of Atea object IDs and may provision workspaces and change platform membership metadata. This role is separate from Microsoft Graph capabilities and never grants Microsoft 365 directory authority. Directory operations require the later Graph capability boundary and customer-granted permissions.

EF migrations run automatically only in Development. Production startup never mutates the database; production migrations are a deployment responsibility.

Feedback stores the submitter's user-entered subject and message as plain text, together with workspace and submitter identity and creation/expiry times. It does not collect credentials or deliver submissions externally. Submissions are isolated by both the active workspace and the authenticated submitter; request data cannot select either identity. A submission expires exactly 90 days after creation, calculated in UTC, and reads hide it as soon as its expiry is reached. A hosted cleanup removes expired submissions from the active database at startup and every 24 hours in batches. This active-database deletion does not guarantee erasure from backups; backup retention is separate.
