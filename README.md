# Playnite Cloud Source

Cloud Source is a Playnite library plugin for discovering and installing games
stored by cloud and network providers. Google Drive is the first provider.

The plugin is intentionally provider-neutral at its core. Provider objects keep
their stable identity and revision, while local installations live below one
validated managed root.

## Current status

The repository contains the first loadable plugin shell:

- Playnite library-plugin entry point;
- provider and source-package contracts;
- one managed storage root with `Games`, `Staging`, and `Cache` children;
- settings validation that rejects relative paths and filesystem roots.

Google Drive authentication and read-only discovery are the next vertical
slice. Installation, cloud deletion, and source mutation are not implemented.

## Build

```powershell
dotnet build .\src\CloudSource.Playnite\CloudSource.Playnite.csproj
```

The project targets .NET Framework 4.6.2 and Playnite SDK 6.16.0.
