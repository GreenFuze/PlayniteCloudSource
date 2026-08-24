# ADR-0006: Use Playnite-native emulation for managed ROMs

- **Status:** Accepted
- **Date:** 2026-08-24

## Context

Cloud Storage must install emulated games without becoming an emulator manager.
ROM archives such as MAME ZIPs are game content, not containers to unpack. Other
ROMs are also normally consumed directly by an emulator. Playnite already owns
configured emulators, built-in and custom profiles, platform identities, ROM
lists, launch-variable expansion, and emulator actions.

The existing archive installer, by contrast, is intentionally a Windows-game
pipeline: download, extract, detect or run an installer, and select an
executable. Reusing that pipeline for ROMs would destroy required archive
structure and duplicate Playnite's emulator configuration.

## Decision

1. Source package type and content type remain separate. ZIP, 7z, and RAR say
   how an object is encoded; path and ROM-extension classification say whether
   it is a native package or emulator content.
2. MAME/Arcade ZIPs and supported single-file ROMs are copied byte-for-byte to
   the managed game directory. They are never sent to an archive extractor.
3. Before opening a cloud download stream, Cloud Storage resolves the platform
   to Playnite's stable platform specification ID and checks configured
   Playnite emulator profiles for both platform and file-extension support.
4. No compatible profile stops installation with guidance and no download. One
   compatible profile is selected automatically. Multiple profiles require an
   explicit player choice.
5. Successful installation populates `Game.Roms` and creates an editable native
   Playnite emulator `GameAction` using the selected emulator and profile IDs.
6. Direct MAME profiles receive the managed ROM directory through `-rompath
   "{ImageDir}"`; other profiles retain their own Playnite arguments.
7. Cloud Storage does not install, configure, update, or directly launch
   emulators. Emulator setup belongs to Playnite and future first-run work.
8. Multi-file disc sets, ScummVM directories, DOSBox packages, BIOS/firmware,
   and shared MAME dependencies require separate content strategies and are not
   inferred as single-file ROMs.

## Consequences

- Native Windows archive and installer behavior remains isolated from emulation.
- ROM files keep the exact format expected by the selected emulator.
- Emulator selection and later edits are visible in Playnite's normal game edit
  UI rather than hidden in plugin settings.
- A missing emulator fails before consuming bandwidth or staging disk space.
- Disc, engine-directory, and dependency-aware systems remain explicit follow-up
  work instead of being partially or incorrectly supported.
