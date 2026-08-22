# Playnite Cloud Source

Cloud Source is a Playnite library plugin for discovering and installing games
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
- recursive, paginated, read-only discovery of ZIP, 7z, and RAR archives;
- stable Playnite game IDs based on provider, account, and Drive object IDs;
- streamed Drive downloads exposed to the future installer pipeline.

Google Drive credentials and authorization are supplied by the player. Cloud
Source does not modify or delete cloud files. Archive extraction, transactional
installation, launch configuration, and local uninstall are not implemented yet.

## Build

```powershell
dotnet build .\src\CloudSource.Playnite\CloudSource.Playnite.csproj
```

The project targets .NET Framework 4.6.2 and Playnite SDK 6.16.0.
