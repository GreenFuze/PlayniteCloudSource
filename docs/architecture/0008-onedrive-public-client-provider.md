# ADR-0008: Add OneDrive as a public-client storage provider

- **Status:** Accepted
- **Date:** 2026-08-24

## Context

Cloud Storage's second provider must prove that the single
`ICloudSourceProvider` facade is an actual extension boundary rather than a
Google-specific abstraction. Microsoft OneDrive exposes files through Microsoft
Graph and authenticates desktop applications through the Microsoft identity
platform.

Xbox and OneDrive can use the same Microsoft account, but they are different
resources. Xbox requires Xbox Live scopes and additional XBL/XSTS exchanges;
OneDrive requires delegated Microsoft Graph file access. Sharing Xbox tokens or
silently broadening Xbox consent would couple unrelated providers and violate
least privilege.

## Decision

1. OneDrive is registered as a second complete `ICloudSourceProvider` facade.
2. Authentication uses authorization-code flow with PKCE, the system browser,
   an explicit account chooser, and a loopback `http://localhost` redirect.
3. The application is a Microsoft public client. The plugin ships its
   publisher-owned application client ID, which is public metadata, and stores
   no client secret. Players are never asked to register an application.
4. Consent is limited to `offline_access`, `User.Read`, and `Files.Read`.
5. Access and refresh tokens are encrypted with Windows DPAPI for the current
   Windows user and committed only when Playnite settings are saved.
6. OneDrive currently browses and scans concrete folders in the account's
   default drive (`My files`). The drive root is browse-only. Shared folders and
   SharePoint document libraries require explicit drive identity support and
   remain follow-up integrations.
7. Microsoft Graph paging links are accepted only from HTTPS
   `graph.microsoft.com/v1.0` URLs and cyclic links fail immediately.
8. Google Drive and OneDrive translate provider records into neutral
   `CloudFileEntry` objects. One `CloudPackageDiscovery` implements archive,
   installer-bundle, and ROM recognition for every storage provider.
9. Xbox authentication code informed the PKCE, account-selection, refresh-token,
   and fail-closed identity behavior, but Xbox scopes and token exchanges are not
   reused by OneDrive.

## Consequences

- Install, uninstall, reconciliation, progress, and metadata code need no
  OneDrive branches.
- Published and local builds use the publisher-owned Microsoft app
  registration, so the player-facing flow is only sign-in and folder choice.
- Supporting shared OneDrive items later will require storing both drive ID and
  item ID because shared items can live in another drive.
