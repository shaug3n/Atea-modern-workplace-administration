# v1 delegated Microsoft Graph permission matrix

The API requests delegated access for the signed-in customer administrator or Atea B2B guest. The browser receives only an API authorization result; Graph access tokens remain server-side and are never returned or logged.

| Delegated scope | v1 operation | Admin consent | Least-privileged tenant role |
| --- | --- | --- | --- |
| `User.Read` | Read the signed-in Graph profile during the Task 4 connection verification | No | Any signed-in user |

The scope name, delegated description and consent flag were checked against the [Microsoft Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference) on 2026-09-18. `User.Read.All`, `Group.Read.All`, `Directory.Read.All` and `RoleManagement.Read.Directory` are deferred to the focused Task 5 feature adapters and are not requested by the Task 4 connection check. No write permission is part of the Task 4 consent challenge.
