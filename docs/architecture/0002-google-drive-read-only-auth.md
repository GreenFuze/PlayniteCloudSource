# ADR-0002: Google Drive read-only authorization and discovery

- **Status:** Accepted
- **Date:** 2026-08-22

## Context

Cloud Source needs stable provider identity and renewable access to a player's
Google Drive without making Playnite or the provider-neutral model depend on
Google-specific SDK types. Connecting from a desktop plugin also means that no
bundled client secret can be treated as confidential.

## Decision

1. The player supplies a Google OAuth client of type **Desktop app**. Cloud
   Source uses the authorization-code flow with PKCE, a cryptographically random
   state value, an exact loopback callback path, and an ephemeral loopback port.
2. The requested scope is `drive.readonly`. This slice has no provider write or
   delete capability.
3. OAuth access and refresh tokens are serialized separately from Playnite
   settings and encrypted with Windows DPAPI for the current user. A staged file
   is committed atomically. OAuth authorization remains a settings draft until
   the player presses Save.
4. Account identity uses Google's stable Drive permission ID. Package identity
   uses the Drive object ID; display names and logical paths are descriptive only.
5. Discovery recursively pages through the configured folder and admits only
   ZIP, 7z, and RAR files. My Drive root is the initial default. A native picker
   for My Drive and Shared with me is deferred to the next slice.
6. Downloads are exposed as response-owned streams for the future staged
   installer. Discovery itself never downloads archive bodies.

## Consequences

- A Google Cloud OAuth client must be configured before the provider can be
  enabled.
- Moving or renaming an archive does not change its imported game identity.
- Local uninstall remains independent from cloud deletion.
- Credentials, folder selection, and provider-specific errors remain inside the
  Google Drive adapter and settings surface.
