# Feature extension contracts

This guide describes the current extension points for adding a feature. It is
not a grant of access or a promise that a reserved feature is available.
Inspect the actual route, capability, module, and service code before adding a
consumer because those contracts can change independently.

## Route and access contract

The frontend route registry is `src/Web/src/app/routes.tsx`. Each `AppRoute`
can declare a `path`, `label`, optional `pageTitle`, `module`, `capability`,
`workspaceAccess`, `navigation`, and a `render` function.

- `module` is checked against the workspace's assigned and enabled modules.
  Current keys are `users`, `devices`, `licenses`, and `exchange`. The route
  type also contains reserved keys for future authentication campaigns,
  license hygiene, About, and Feedback; that type union does not activate
  those features.
- `workspaceAccess` checks workspace-management privileges (`members`,
  `settings`, `modules`, or `any`). It is not a substitute for a module or
  capability check.
- `capability` is evaluated from the workspace-scoped capability snapshot.
  `App.tsx` fails closed while that snapshot is loading, unavailable, not
  Graph-authoritative, belongs to a different workspace, or does not
  allow/read-only the requested capability.
- `navigation` controls presentation only. `PrimaryNav` applies its visibility
  rules (`always`, `module`, `device-module-or-settings-manager`,
  `audit-not-hidden`, or `workspace-manager`); hiding a link is not
  authorization.

Add each new route deliberately: select its module and capability contracts,
decide whether workspace-management access applies, and set navigation
metadata only if the route belongs in the sidebar. The API endpoint must
enforce its own authorization and workspace context; client route metadata
does not protect an API. Verify the route and API with the feature's tests.

## Reserved capabilities and future activation

`src/Web/src/capabilities/capabilityTypes.ts` and
`src/Api/Authorization/Capability.cs` keep matching reserved capability
identifiers for authentication campaigns, license hygiene, About, and
Feedback. The reserved lists are design reservations, not active capability
decisions. Likewise, a candidate module name in the `AppRoute` type is not an
enabled workspace module. Do not add placeholder pages, navigation entries,
or effective grants merely because an identifier exists.

Before a future feature is activated, its owner must intentionally validate
and wire its authoritative source, least-privilege permissions, applicable
Entra roles and PIM requirements, tenant behavior, failure states, and audit
needs. Then add the feature-owned route and module/capability checks, API
authorization, UI messages, dependency registrations, endpoint mappings, and
tests. Keep unknown source, scope, RBAC, PIM, tenant-policy, and data-retention
limits explicit until validated. A feature is not shipped or tenant-enabled
by documenting or reserving its name.

## Feature-owned messages

The application catalog is `src/Web/src/messages/en.ts`; `MessageKey` in
`src/Web/src/app/messages.ts` is derived from the composed `messages` object.
Overview keeps feature copy in
`src/Web/src/features/overview/messages.ts` as a uniquely named
`overviewMessages` object, and the application catalog imports and explicitly
composes it.

Keep new copy in a feature-owned module and export a feature-specific object
name with feature-prefixed keys. When shared app copy is needed, explicitly
compose that object into `messages/en.ts` and use the derived typed catalog.
Avoid generic exports such as multiple feature files exporting `messages`,
and avoid duplicate keys when composing objects: a later spread can silently
replace an earlier value. Extend the typed-catalog regression test when new
visible shared copy is introduced.

## Backend registration and endpoint mapping

The Overview feature shows the current two-stage API composition:

- `OverviewFeatureServiceCollectionExtensions.AddOverviewFeature` registers
  its services with dependency lifetimes (the cache is singleton; request
  services are scoped).
- `Program.cs` calls `builder.Services.AddOverviewFeature()` while building
  the service collection.
- Separately, after the app is built, `Program.cs` calls
  `app.MapOverviewEndpoints()`. `OverviewEndpoints.MapOverviewEndpoints`
  maps the authorized HTTP route.

For a new feature, put feature-owned registrations behind a named
`Add<Feature>Feature` extension and HTTP mapping behind a separate
`Map<Feature>Endpoints` extension. Add each call to the appropriate
`Program.cs` lifecycle location. Service registration does not expose routes;
endpoint mapping does not register dependencies. Choose lifetimes to match
the dependency graph, and do not let a singleton capture a scoped service.
Keep endpoint authorization and workspace-specific decisions in the API
boundary rather than relying on navigation or frontend state.

## Shared UI primitives

Use the existing shared components where their semantics fit; their props are
the current API, not a reason to skip feature-specific authorization,
accessibility, or audit checks.

| Need | Existing component and contract |
| --- | --- |
| Access decision | `PermissionState` accepts a `CapabilityDecision`, children, and optional `reasonText`; hidden decisions render nothing, allowed decisions render children, and other states disable interactive descendants and explain the state. `DomainAccessChip` accepts a label and view decision/reason, with optional write decision/reason. `DisabledReason` accepts children, a required reason, and optional source of truth. |
| Freshness | `DataFreshness` accepts a freshness state (`fresh`, `stale`, or `unavailable`), `partialData`, optional fetch time, message, labels, presentation, source, and refresh callback/state. Preserve the source response's actual freshness; do not label unavailable or partial data as fresh. |
| Filters | `StatusPillFilter` is controlled by a label, option values/labels/counts/selected state, and `onToggle(value)`. `KpiFilterTile` is a controlled button with label, optional value/detail, selected state, and `onClick`. Keep the result set and selected state in the feature that owns the query. |
| Help | `InfoTip` accepts an accessible label and content; it exposes a button and a dialog popover. `ExplainerPanel` accepts a title, children, and optional id for persistent page-level explanation. |
| Reasons | `ReasonDialog` extends the confirmation-dialog props with `onConfirm(reason)` and optional `reasonHint`; it trims and requires a non-empty reason before invoking the callback. The caller must submit that reason through the approved operation and preserve that operation's audit behavior. A required text field by itself is not an audit record. |
| Loading | `WorkspaceDataState` supports loading, empty, unavailable, partial, permission, and stale states with optional retry/action details. `LoadingSkeleton` accepts an optional label and line count; the shared skeleton styling disables shimmer for reduced-motion preferences. Both expose status semantics for their own rendered state. |

The async-state components do not set `aria-busy` on the page section, table,
or result region that is being refreshed. The feature caller owns that
semantic: set `aria-busy={loading}` on the region whose content is loading,
and render an appropriate state or skeleton inside it. Do not mark an entire
page busy when only one independently loaded section is updating.

Existing consumers worth checking when changing shared behavior include
`OverviewPage` (overview loading, unavailable, empty and freshness states),
`UsersPage`, `UserDetailPage`, `DevicesPage`, `DeviceDetailPage`, and
`AuditActivityPage` (data and access states). In this branch, the existence
of a primitive such as `InfoTip`, `StatusPillFilter`, `ReasonDialog`, or
`LoadingSkeleton` does not establish that every feature uses or validates that
pattern. These primitives, along with `DomainAccessChip`, `DisabledReason`,
`ExplainerPanel`, and `KpiFilterTile`, have no direct feature-page call site in
this branch. Inspect real call sites and tests before treating a pattern as
deployed behavior. Any new consumer should verify keyboard and screen-reader
behavior, reason handling and audit integration where applicable, and
graceful partial/unavailable states.
