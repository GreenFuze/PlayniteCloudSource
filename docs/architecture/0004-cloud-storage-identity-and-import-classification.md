# ADR-0004: Cloud Storage identity and import classification

- **Status:** Accepted
- **Date:** 2026-08-23

## Context

The original product name, Cloud Source, described where packages came from but
not the managed installation and cache responsibility the plugin will own. The
first real Google Drive scan also showed that treating every archive as a game
discarded useful MGA path structure and admitted save-sync snapshots.

## Decision

1. The user-facing product and Playnite library source are named **Cloud
   Storage**. The plugin ID, assembly, namespaces, settings path, and stable game
   IDs remain unchanged for compatibility.
2. Startup migrates the existing Playnite source name in place. It does not move
   an existing managed root or replace user-edited metadata.
3. Archive titles use the conservative MGA title-cleaning rules: remove trailing
   dump/region/version noise, remove a setup prefix, and turn filename separators
   into readable spaces while preserving meaningful sequel numbers.
4. A `Platforms/<name>` path assigns the corresponding Playnite platform.
   `Installers` assigns `PC (Windows)`.
5. Archives below `mga_save_sync` or `mga_sync` are not game packages and are
   excluded from future imports.
6. Existing false-positive records are not deleted automatically. Removing them
   is a separate, explicit user-authorized cleanup.
7. MAME machine IDs are not guessed from generic metadata. Arcade identification
   will reuse MGA's MAME DAT approach in a dedicated resolver.

## Consequences

- Existing Cloud Storage game identities, OAuth state, folder selection, and
  managed-root configuration survive the rename.
- Playnite metadata lookup receives substantially better PC and console titles
  plus platform hints.
- Arcade games remain visibly incomplete until the MAME DAT resolver lands, but
  they are no longer misrepresented as ordinary platform-unknown archives.
- Sync snapshots stop reappearing after a player explicitly removes the legacy
  false-positive records.
