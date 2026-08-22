# ADR-0001: Cloud Source plugin boundaries

- **Status:** Accepted
- **Date:** 2026-08-22

## Context

Cloud Source must import provider-backed packages into Playnite and eventually
own download, install, play, and uninstall actions. Google Drive is first, but
the design must admit SMB and other providers without making Playnite records
or installation code Google-specific.

## Decision

1. Cloud Source is one Playnite `LibraryPlugin`. Provider adapters implement a
   small object-oriented contract for scanning and opening immutable source
   packages.
2. Every source package retains provider ID, account identity, object identity,
   revision, logical path, size, and package kind. Display names are never used
   as provider identity.
3. All plugin-managed game content is placed below one configured absolute
   root. The root owns `Games`, `Staging`, and `Cache` directories. Filesystem
   roots are rejected.
4. Local uninstall and provider deletion are different operations. Uninstall
   may remove only a manifest-owned path below the managed root. A future cloud
   deletion action will require a separate preview and explicit confirmation.
5. Future installers stage and validate content before committing a final game
   directory, matching MGA's managed-installation boundary.
6. Playnite Bridge is a development and verification surface. Cloud Source has
   no runtime dependency on it and talks to Playnite through the official SDK.

## First vertical slice

The first provider slice authenticates Google Drive, lets the player select a
folder, discovers supported standalone archives, and imports them as
uninstalled Playnite games. It does not download, extract, execute, uninstall,
or delete provider content.
