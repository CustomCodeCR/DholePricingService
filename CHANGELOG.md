# Changelog

## [1.2.8] - 2026-11-06

- Persists independent pickup and delivery addresses for inland transportation services in the rate API, database and response.
- Keeps geographic coordinates optional and preserves existing location information for legacy API callers.
- Adds a compatible JSONB migration for the new structured service locations.

## [1.2.7] - 2026-11-06

- Resolves Dynamics new_equipo aliases for 26/48/53-foot FTL equipment when Pricing writes pies, feet or ft and the Dataverse catalog uses quote marks.
- Fixes QUO-00000-000546 equipment mapping without changing RateId, IDTRA, currency, commercial totals, quote revision or idempotency.
- Rejects mismatched trailer lengths, tonnage and FCL/FTL choices; adds integration regression tests.

## [1.2.6] - 2026-11-06

- Impide el cierre automático de solicitudes de modificación por avances de revisión o por el estado comercial de la QUO.
- Exige confirmación explícita para cancelar modificaciones y recupera únicamente cancelaciones huérfanas sin auditoría.
- Conserva solicitudes abiertas durante preparación, SLA y trabajo del proveedor; agrega pruebas de regresión.


## [1.2.5] - 2026-11-05

- Sustituye confirmación manual por despliegue automático al integrar master.
- Mantiene verificaciones de Dynamics, EF, imágenes y healthchecks antes y después del deploy.
- Incrementa SemVer de Pricing y metadatos del ecosistema.

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
