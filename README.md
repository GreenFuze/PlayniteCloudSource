# Playnite Cloud Storage

Cloud Storage is a Playnite library plugin for discovering and installing games
stored by cloud and network providers. Google Drive and Microsoft OneDrive are
implemented providers.

The plugin is intentionally provider-neutral at its core. Each integration
registers one complete `ICloudSourceProvider` facade for connection, folder
selection, authoritative scanning, and package streams. Provider objects keep
their stable identity and revision, while local installations live below one
validated managed root.

## Current status

The current vertical slice provides:

- a loadable Playnite library plugin with provider-neutral contracts;
- one managed storage root with `Games`, `Staging`, and `Cache` children;
- Google OAuth desktop-app authorization with PKCE and state validation;
- Windows-user-bound encryption for access and refresh tokens;
- a theme-aware Google Drive folder picker for My Drive and Shared with me;
- Microsoft public-client authorization with PKCE, explicit account selection,
  and delegated `Files.Read`/`User.Read` access;
- a OneDrive My files folder picker and recursive Microsoft Graph discovery;
- MGA-style archive title cleanup and path-derived Playnite platforms;
- exclusion of MGA sync storage from the game archive inventory;
- recursive, paginated, read-only discovery of ZIP, 7z, and RAR archives;
- discovery of common single-file cartridge ROM formats;
- stable Playnite game IDs based on provider, account, and Drive object IDs;
- streamed Drive downloads;
- transactional ZIP, 7z, and RAR installation into the managed `Games` directory;
- byte-preserving ROM installation with pre-download Playnite emulator/profile
  validation and native Playnite ROM/emulator actions;
- traversal/link/encryption/size validation and MGA-style executable selection;
- manifest-validated launch and uninstall actions;
- authoritative successful-scan reconciliation: missing uninstalled games are
  removed, while installed games are retained and tagged as source unavailable.

Google Drive developer credentials, provider authorization, and a concrete
source folder are supplied by the player. OneDrive uses the plugin publisher's
public application ID, so players only sign in and choose a folder. Drive roots
are browse-only to prevent accidental whole-drive imports. OneDrive currently
scans folders in My files; shared-item
collections are not yet selectable. Cloud Storage does not modify or delete cloud files. Game
archives are installed and removed locally; password-protected and multi-volume
archives are rejected. Multi-file disc sets, ScummVM directories, BIOS/firmware,
and shared emulator dependencies remain follow-up content strategies.

Generic Playnite metadata providers can identify ordinary PC and console titles
after normalization. Arcade archives that use MAME machine IDs require a MAME
DAT resolver; that resolver is intentionally a separate follow-up slice.

## Build

```powershell
dotnet build .\src\CloudSource.Playnite\CloudSource.Playnite.csproj
dotnet run --project .\tests\CloudSource.Playnite.Tests\CloudSource.Playnite.Tests.csproj
```

The project targets .NET Framework 4.6.2 and Playnite SDK 6.16.0.
