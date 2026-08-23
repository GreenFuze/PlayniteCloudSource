# 0005: Safe archive extraction on Playnite 10

## Status

Accepted for the Playnite 10 plugin.

## Context

Cloud Storage must extract ZIP, 7z, and RAR packages that should be treated as
untrusted input. Playnite 10 already loads the strong-named SharpCompress 0.26
assembly. Playnite's extension documentation warns that extensions cannot load
multiple versions of the same assembly, so shipping a current SharpCompress
beside the host version is not reliable.

SharpCompress 0.26 is covered by two directory-traversal advisories:

- <https://github.com/advisories/GHSA-jp7f-grcv-6mjf>
- <https://github.com/advisories/GHSA-6c8g-7p36-r338>

Both advisories affect SharpCompress's directory extraction helpers. Cloud
Storage does not call those helpers.

## Decision

The plugin compiles against SharpCompress 0.26 without packaging its runtime
assembly. The standalone test executable references the same version directly.

Cloud Storage owns the complete extraction policy and write path:

- enumerate and validate every entry before writing anything;
- reject rooted paths, drive-qualified paths, `.` and `..` segments, duplicate
  destinations, links, encrypted entries, split entries, and incomplete or
  multi-volume archives;
- cap entry count and total expanded size;
- verify available disk space;
- stream each file to a prevalidated full path using create-new semantics;
- verify every extracted byte count; and
- remove the entire staging directory on success, cancellation, or failure.

## Consequences

The vulnerable SharpCompress extraction helpers remain unreachable from this
plugin, while 7z and RAR decompression stays compatible with Playnite 10's
application domain. The long-term solution is a Playnite core dependency
upgrade; that work is tracked separately from this plugin.
