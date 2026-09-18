# v1 delegated Microsoft Graph permission matrix

The API requests delegated access for the signed-in customer administrator or Atea B2B guest. The browser receives only an API authorization result; Graph access tokens remain server-side and are never returned or logged.

| Delegated scope | v1 operation | Admin consent | Least-privileged tenant role |
| --- | --- | --- | --- |
| `User.Read` | Read the signed-in Graph profile during connection verification | No | Any signed-in user |
| `User.Read.All` | Read user profiles for the future users view | Yes | Directory Readers |
| `Group.Read.All` | Read groups and memberships for the future groups view | Yes | Directory Readers |
| `Directory.Read.All` | Read directory objects needed to correlate users, groups and tenant metadata | Yes | Directory Readers |
| `RoleManagement.Read.Directory` | Read directory role definitions, assignments and PIM visibility | Yes | Directory Readers (additional role visibility may require Privileged Role Administrator) |

The scope names, delegated descriptions and admin-consent flags were checked against the [Microsoft Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference) on 2026-09-18. `Directory.Read.All` is intentionally documented as broad and should be reduced when a later focused adapter proves narrower scopes sufficient. No write permission is part of the Task 4 consent challenge.
