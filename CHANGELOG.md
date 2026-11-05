# Changelog

## [1.2.5] - 2026-11-05

- Removes the redundant confirmation checkbox and typed approval gate from manual Pricing production releases; choosing Run workflow on master remains the release action.
- Retains explicit manual-only production invocation, master branch validation, pre-deploy FTL image integrity check, non-cancelling releases and all existing health checks.
- Updates production workflow policy regression assertions to prevent duplicate confirmations without allowing unreviewed push-triggered production deployments.

## [1.2.4] - 2026-11-05

- Allows creating client QUOs from management-approved master tariffs before their validity start date, preserving both master validity dates and the exact commercial snapshot.
- Continues to reject expired tariffs, unapproved masters and non-master or spot rates.
- Adds automated regression coverage for future-effective, currently valid, expired and unauthorized tariff application, with Pricing API build verification.

## [1.2.3] - 2026-11-05

- Removes the automatic `push` trigger from Pricing's production deploy workflow; production jobs no longer appear as skipped after each merge.
- Requires an explicit, checked confirmation and the `master` branch for every manual production deployment.
- Blocks production container replacement unless the built Pricing image contains the active FTL 5-ton category mapping and Docker Compose does not override it incorrectly.
- Preserves existing Dynamics credential checks, EF migration validation, health probes and quote report smoke tests.
- Aligns production workflow across `develop` and `master`, adds dedicated policy CI and prevents cancellation of an active release.


## [1.2.2] - 2026-11-05

- Enables an explicit, existing Dataverse 24-foot / 7-ton capacity category for Pricing 24-foot / 5-ton FTL requests, without inventing a Dynamics option.
- Preserves the original 5-ton equipment text in `new_cantidadequipos` and never modifies rates, totals, IDTRA or RateId.
- Adds environment-specific mapping configuration and integration regression tests for Development, Staging and Production.
- Keeps all other unrecognized or ambiguous equipment choices blocked instead of guessing.


## [1.2.1] - 2026-11-05

- Fixes Dynamics `new_equipo` resolution across the 22 catalog labels (FCL, FTL, LCL, LTL).
- Normalizes suffixes and aliases, while preserving full container combinations and tonnage.
- Supports validated, explicitly approved equipment alias configuration for catalog mismatches.
- Prevents 5-ton/24-foot vehicles from silently being reported as 7-ton/24-foot equipment.
- Adds integration regression tests for catalog options, ambiguous matches and overrides.


## [1.2.0] - 2026-11-05

- Incorporates universal Dhole SemVer policy with GitHub Actions checks.
- Synchronizes service assembly metadata with the ecosystem version.
- Adds permanent Codex instructions and coordinated release requirements.
