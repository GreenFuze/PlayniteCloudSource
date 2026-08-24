# Cloud Storage privacy policy

**Effective date:** 2026-08-24

Cloud Storage is a local Playnite extension. It has no developer-operated
server, telemetry, analytics, advertising, or user-tracking system. The plugin
author does not collect, receive, sell, or share personal data through the
extension.

## Data accessed

When a player connects a provider, Cloud Storage requests read-only access to
the selected cloud account:

- Google Drive: `drive.readonly`.
- Microsoft OneDrive: delegated `Files.Read` and `User.Read`.

The plugin reads account identity, folder and file metadata, and the contents of
files that must be downloaded for installation. It does not request provider
write or delete permissions and does not modify cloud files.

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

Disconnecting a provider removes its locally stored authorization. Uninstalling
the extension removes its installed program files; Playnite may retain extension
settings or data according to Playnite's own uninstall and backup behavior.
Players can also revoke Cloud Storage from their Google or Microsoft account's
connected-app settings.

## Contact

Privacy questions and deletion concerns can be submitted through the public
[GitHub issue tracker](https://github.com/GreenFuze/PlayniteCloudSource/issues).
