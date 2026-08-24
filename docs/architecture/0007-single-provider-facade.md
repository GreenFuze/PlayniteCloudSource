# ADR-0007: Expose each storage integration through one provider facade

- **Status:** Accepted
- **Date:** 2026-08-24

## Context

The first Google Drive implementation already had a package and stream provider
interface, but the Playnite plugin still constructed Google-specific scan
requests and directly coordinated authorization and folder selection. Adding a
second integration in that shape would spread provider branches through the
core and make every later provider more expensive.

A single concrete class containing OAuth, HTTP, paging, JSON, and UI logic would
avoid interface sprawl but become equally difficult to maintain. Those details
need composition without becoming dependencies of the Cloud Storage core.

## Decision

1. Every integration registers one complete `ICloudSourceProvider` facade.
2. The facade owns provider identity, configuration state, connection draft
   lifecycle, source-folder selection, authoritative scans, and package streams.
3. Provider-specific OAuth services, API clients, serializers, folder browsers,
   and dialogs are private implementation composition. The plugin core never
   coordinates or depends on them individually.
4. A provider returns one or more authoritative account scan results. Each
   result must contain only packages with the same provider and account identity;
   mismatches fail immediately.
5. `CloudSourcePlugin` iterates configured providers and scan results without
   branching on provider IDs. Classification, metadata, reconciliation,
   installation, and uninstall remain provider-neutral.
6. The application composition root may know concrete provider classes in order
   to construct and register them. Runtime workflows may not.
7. No provider base class is introduced until multiple implementations reveal
   genuinely identical reusable behavior. The stable contract is the interface.

## Consequences

- OneDrive can be implemented and registered as a second facade without changes
  to package installation or library reconciliation.
- Google Drive retains focused internal classes without leaking a collection of
  helper interfaces into the plugin core.
- Account and package ownership errors are rejected at the provider boundary.
- Provider-specific settings fields still require UI presentation, but their
  operations execute through the same provider facade.
