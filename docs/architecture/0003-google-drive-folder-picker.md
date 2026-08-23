# ADR-0003: Google Drive source folder picker

- **Status:** Accepted
- **Date:** 2026-08-23

## Context

Cloud Source needs a safe way to choose a Google Drive subtree without asking
players to copy opaque Drive object IDs. Google Drive presents My Drive and
Shared with me differently, while discovery still requires a stable folder ID
that survives renames and moves.

## Decision

1. The picker presents Google Drive as a provider root with My Drive and Shared
   with me as peer locations.
2. My Drive root, Shared with me, and the provider root are browse-only. Only a
   concrete child folder can be saved, preventing an accidental whole-drive
   scan.
3. Navigation is paginated and uses Drive object IDs internally. Settings store
   the selected folder ID and a separate friendly display path.
4. Shared with me is a virtual collection rather than a selectable folder. A
   concrete shared folder is selectable after opening the collection.
5. Back navigation reuses already loaded folder pages during the picker session.
6. The picker inherits Playnite's `TextBrush` theme resource so normal text
   remains readable across desktop themes.
7. Changing or disconnecting the Google account clears the previous folder
   selection because Drive object IDs are scoped to the connected account.

This decision supersedes ADR-0002's temporary use of My Drive root as the
default source.

## Consequences

- Enabling Google Drive requires a connected account and a concrete source
  folder.
- Renaming or moving the selected folder does not invalidate its stored
  identity, although its saved display path may be stale until reselected.
- Cloud Source can add future providers behind the same browse-and-select model
  without exposing provider-specific IDs in the settings UI.
- Browsing remains read-only and does not create, move, or delete Drive items.
