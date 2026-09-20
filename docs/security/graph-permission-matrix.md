# v1 delegated Microsoft Graph permission matrix

The API requests delegated access for the signed-in customer administrator or Atea B2B guest. The browser receives only an API authorization result; Graph access tokens remain server-side and are never returned or logged.

| Delegated scope | v1 operation | Admin consent | Least-privileged tenant role |
| --- | --- | --- | --- |
| `User.Read` | Read the signed-in Graph profile during the connection verification | No | Any signed-in user |
| `User.Read.All` | Search/read another user's core directory profile fields and direct memberships | Yes | Directory Readers, Global Reader, User Administrator, or another role/custom role granting user read |
| `Group.Read.All` | Read groups and group metadata needed to render user memberships | Yes | Directory Readers, Global Reader, Groups Administrator, or another role/custom role granting group read |
| `GroupMember.Read.All` | Read group memberships and permission grants where least-privilege group membership access is sufficient | Yes | Directory Readers, Global Reader, Groups Administrator, or another role/custom role granting membership read |
| `Directory.Read.All` | Read directory objects when Graph relationship responses require richer object data across users, groups, roles, and service principals | Yes | Directory Readers or Global Reader |
| `User.Create` | Create non-admin users for approved lifecycle workflows | Yes | User Administrator for non-admin users; higher privileged role for sensitive/admin targets |
| `User.ReadWrite.All` | Update writable user properties where the operation requires broad user write access | Yes | User Administrator for non-admin user lifecycle operations; higher privileged role for sensitive/admin targets |
| `User.EnableDisableAccount.All` + `User.Read.All` | Disable/reactivate user accounts | Yes | Privileged Authentication Administrator for all administrators; User Administrator is sufficient only for supported non-admin password/profile scenarios |
| `User-PasswordProfile.ReadWrite.All` | Set temporary password profile during user creation/reset workflows | Yes | User Administrator for non-admin users; Privileged Authentication Administrator for admins |
| `GroupMember.ReadWrite.All` | Add/remove users from groups | Yes | Group owners, Groups Administrator, User Administrator, Directory Writers, or another supported role for the target group type; Privileged Role Administrator for role-assignable groups |
| `LicenseAssignment.ReadWrite.All` | Assign/remove user or group licenses | Yes | License Administrator, User Administrator, or Directory Writers |
| `RoleManagement.ReadWrite.Directory` | Create role assignments and support role/PIM activation flows where policy allows | Yes | Privileged Role Administrator |

The scope name, delegated description and consent flag were checked against Microsoft Learn on 2026-09-20:

- [Microsoft Graph permissions overview](https://learn.microsoft.com/en-us/graph/permissions-overview)
- [Create user](https://learn.microsoft.com/en-us/graph/api/user-post-users?view=graph-rest-1.0)
- [Update user](https://learn.microsoft.com/en-us/graph/api/user-update?view=graph-rest-1.0)
- [List a user's direct memberships](https://learn.microsoft.com/en-us/graph/api/user-list-memberof?view=graph-rest-1.0)
- [Add group members](https://learn.microsoft.com/en-us/graph/api/group-post-members?view=graph-rest-1.0)
- [Assign user license](https://learn.microsoft.com/en-us/graph/api/user-assignlicense?view=graph-rest-1.0)
- [Create directory role assignment](https://learn.microsoft.com/en-us/graph/api/rbacapplication-post-roleassignments?view=graph-rest-beta)

The adapter requests scopes per operation through `IDelegatedGraphClientFactory`; the frontend never receives Graph scopes, tokens, raw Graph payloads, or Authorization headers. Later feature tasks must keep using these operation-specific scope sets instead of broadening the connection-health check.
