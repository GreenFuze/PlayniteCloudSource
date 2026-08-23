# Playnite Cloud Storage

Cloud Storage is a Playnite library plugin for discovering and installing games
stored by cloud and network providers. Google Drive is the first provider.

The plugin is intentionally provider-neutral at its core. Provider objects keep
their stable identity and revision, while local installations live below one
validated managed root.

## Current status

The current vertical slice provides:

- a loadable Playnite library plugin with provider-neutral contracts;
- one managed storage root with `Games`, `Staging`, and `Cache` children;
- Google OAuth desktop-app authorization with PKCE and state validation;
- Windows-user-bound encryption for access and refresh tokens;
- a theme-aware Google Drive folder picker for My Drive and Shared with me;
- MGA-style archive title cleanup and path-derived Playnite platforms;
- exclusion of MGA sync storage from the game archive inventory;
- recursive, paginated, read-only discovery of ZIP, 7z, and RAR archives;
- stable Playnite game IDs based on provider, account, and Drive object IDs;
- streamed Drive downloads exposed to the future installer pipeline.

Google Drive credentials, authorization, and a concrete source folder are
supplied by the player. Drive roots are browse-only to prevent accidental
whole-drive imports. Cloud Storage does not modify or delete cloud files. Archive
extraction, transactional installation, launch configuration, and local
uninstall are not implemented yet.

Generic Playnite metadata providers can identify ordinary PC and console titles
after normalization. Arcade archives that use MAME machine IDs require a MAME
DAT resolver; that resolver is intentionally a separate follow-up slice.

## Build

```powershell
dotnet build .\src\CloudSource.Playnite\CloudSource.Playnite.csproj
```

The project targets .NET Framework 4.6.2 and Playnite SDK 6.16.0.
