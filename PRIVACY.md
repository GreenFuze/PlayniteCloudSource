# Cloud Storage privacy policy

**Effective date:** 2026-08-25

Cloud Storage is a local Playnite extension. It has no developer-operated
server, telemetry, analytics, advertising, or user-tracking system. The plugin
author does not collect, receive, sell, or share personal data through the
extension.

## Data accessed

When a player connects a provider, Cloud Storage requests read-only access:

- Google Drive: restricted `drive.readonly`, which technically permits reading
  all files in the connected Drive. This is required because Google's per-file
  scope cannot enumerate existing descendants of a selected folder.
- Microsoft OneDrive: delegated `Files.Read` and `User.Read`.

The plugin reads account identity, folder and file metadata, and the contents of
files that must be downloaded for installation. Cloud Storage's code limits
Google discovery and downloads to the concrete folder selected through Google
Drive's Playnite-native folder browser, even though the OAuth scope is broader. The plugin does not modify or
delete cloud files.

## Local storage

Cloud Storage keeps the following information on the player's computer:

- provider account identifier and display name;
- selected folder identifiers and display paths;
- encrypted OAuth access and refresh tokens;
- installation manifests and locally installed game files; and
- ordinary diagnostic information written through Playnite's logging system.

OAuth tokens are stored separately from plugin settings and encrypted with
Windows Data Protection API (DPAPI) for the current Windows account. The plugin
does not store account passwords.

## Data sharing and retention

Authentication and file requests are sent directly from the player's computer
to Google or Microsoft over HTTPS. Those providers process data under their own
privacy policies. Cloud Storage does not send user data or OAuth tokens to the
plugin author or to any other third party.

Cloud Storage's use of information received from Google Workspace APIs adheres
to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy),
including its Limited Use requirements. Provider data is used only for the
visible library-discovery and game-installation features requested by the
player. It is not sold, used for advertising, used for credit decisions, or
used to train generalized AI or machine-learning models.

Disconnecting a provider and saving the settings removes its locally stored
authorization, account identity, and selected-folder configuration. Cloud
Storage also attempts to revoke Google authorization remotely. Players can
review or revoke access directly in [Google Account connections](https://myaccount.google.com/connections)
or [Microsoft account consent management](https://account.live.com/consent/Manage).
Work or school Microsoft accounts can use [My Apps](https://myapps.microsoft.com/).

Playnite may retain extension settings or data according to its own uninstall
and backup behavior. Before uninstalling Cloud Storage, disconnect each provider
and save the settings. To remove every remaining local copy, delete Cloud
Storage's Playnite extension-data directory and the configured managed storage
root. The managed root can contain installed games and save data, so inspect it
before deleting it. None of these actions deletes files from Google Drive or
OneDrive.

## Contact

Privacy questions and deletion concerns can be submitted through the public
[GitHub issue tracker](https://github.com/GreenFuze/PlayniteCloudSource/issues).
