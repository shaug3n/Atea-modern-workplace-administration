# Audit Retention and Incident Triage

The platform audit stream records workspace-scoped activity performed through Atea Unified Workplace. Microsoft 365 audit logs remain the authoritative source for directory changes; this platform stream records actor intent, result, and correlation references needed to connect an in-app action to Microsoft 365 evidence.

## Retention

- Retain platform audit events for the workspace retention period approved by the customer contract and security policy.
- Keep retained events workspace-scoped by `workspaceId` and `tenantId`; do not export one customer's events into another customer's investigation package.
- Preserve `timestamp`, `actorObjectId`, `action`, `targetType`, `targetId`, `outcome`, `failureCategory`, and correlation fields for the full retention period.
- Use Microsoft 365 Purview audit retention for authoritative directory-operation history. The platform stream is supporting evidence, not a replacement for Purview.

## Redaction

- Store only safe metadata in `safeMetadataJson`.
- Do not store access tokens, refresh tokens, client secrets, authorization headers, temporary passwords, raw Microsoft Graph payloads, or full exception messages.
- Never include feedback content, including its subject or message, or diagnostic payloads in audit events, logs, or telemetry.
- Redact sensitive keys before writing audit metadata. Safe examples include non-secret changed field names, high-level request status, role display names, SKU identifiers, and `[REDACTED]` markers.
- UI and API error responses must use stable codes and safe messages. Use the correlation ID for investigation instead of exposing upstream Graph messages to operators.

## Correlation

- Every API response includes `X-Correlation-ID`. A valid caller-supplied value may be reused; unsafe values are replaced by a generated server value.
- Store the platform `correlationId` on audit events whenever an operation is initiated through the API.
- Store Microsoft Graph `request-id` and `client-request-id` only as correlation references. Do not store the Graph response body.
- During incident review, correlate in this order: platform `correlationId`, Graph request IDs, PIM request ID when present, then the Microsoft 365 audit log entry for the target tenant and timestamp.

## Incident Triage

1. Identify the affected workspace and tenant before querying audit activity.
2. Filter by `action`, `actorObjectId`, `targetId`, and time window.
3. Check `outcome` and `failureCategory` to separate completed changes from denied, throttled, or transient operations.
4. Use correlation IDs to locate API logs and Microsoft Graph/PIM evidence.
5. Confirm the authoritative result in Microsoft 365 audit logs before declaring whether a directory change occurred.
6. Share only redacted excerpts in tickets or customer updates. Include correlation IDs, timestamps, action names, and high-level outcomes; exclude secrets and raw upstream payloads.
