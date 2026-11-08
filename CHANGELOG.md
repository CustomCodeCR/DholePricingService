# Changelog

## [1.6.0] - 2026-11-08 (candidato F6.02, no publicado)

- Añade invalidaciones mínimas para cambios de RateHeader al Outbox existente de Pricing y las envía a un stream separado dhole.pricing.realtime.v1; no sustituye cachés ni los eventos comerciales actuales.
- Permite productor realtime únicamente con los cuatro flags del release rt-20261107-01, apagados por defecto; los cambios y eventos se confirman dentro del mismo SaveChanges de EF.
- Mantiene REST autoritativo y registros de costos/cliente fuera del payload de invalidación; incorpora pruebas de contrato, seguridad, versionado y compilación de API y Worker.
- F6.02 sigue candidata en rama intermedia hasta completar adaptador Gateway, pruebas de entrega y matriz completa de mutaciones; no integra develop/master ni despliega.
- Versión del componente 1.5.7 -> 1.6.0; versión global propuesta 1.13.0.

## [1.5.7] - 2026-11-08

- PATCH coordinado con DholeWeb 1.12.4 para corregir la presentación FCL de ofertas comercialmente repetidas y la expansión de sub-QUO guardadas en modo view.
- No cambia el contrato REST ni la persistencia de Pricing; conserva el manejo idempotente de la sub-QUO final y PDF individual de releases anteriores.
- Release `DH-20261108-PRICING-VIEW-ACCORDION-FCL-UNIQUE-035`, sujeto a CI y staging.

## [1.5.6] - 2026-11-08

- Release coordinado con DholeWeb v1.12.2 para presentar una sola tarjeta por oferta FCL equivalente con vigencias superpuestas y sin eliminar registros de Pricing.
- Promueve a master la finalización irrevocable de multiQUO de 1.5.5: la alternativa seleccionada se renombra a la QUO comercial sin sufijo, se bloquean las demás y el PDF pasa a individual.
- Conserva compatibilidad de APIs REST y validaciones de versión, estado e integridad de migraciones.
- Release `DH-20261108-FCL-EQUIVALENT-RATES-033` sujeto a CI y staging.

## [1.5.5] - 2026-11-08

- Finalización irrevocable de multiQUO FCL: la subcotización seleccionada adquiere la QUO base, conserva su RateId y deja de permitir seleccionar o modificar alternativas descartadas.
- PDF individual del registro definitivo desde cualquier enlace del grupo; el PDF múltiple solo aplica antes de la elección.
- Migración de grupos previamente seleccionados sin alterar el parent_rate_id ni los historiales; controles de estado comercial para las alternativas descartadas.
- Release DH-20261108-MULTIQUO-FINAL-INDIVIDUAL-032, compatible con DholeWeb v1.11.6, sujeto a CI y staging.

## [1.5.4] - 2026-11-07

- Publica selectedRateId en los grupos de QUO y prioriza la sub-QUO seleccionada en los PDF sin modificar la identidad del grupo.
- Release `DH-20261107-MULTIQUO-PRINCIPAL-DEDUP-ACCORDION-030` coordinado con DholeWeb v1.11.5; requiere CI y staging, sin autorización de producción.

## [1.5.3] - 2026-11-07

- Versionado coordinado de compatibilidad del contrato MultiQUO para el traspaso del Inland seleccionado por cada POE y naviera, sin cambios incompatibles de API.
- Release `DH-20261107-FCL-HAULAGE-HANDOFF-028`. Integración primero en `develop`, validación staging y promoción del mismo lote a `master`.

## [1.5.2] - 2026-11-07

- El índice PDF multiQUO utiliza el Value comercial de la moneda, no el identificador CUR interno; añade pruebas de regresión.
- Release DH-20261107-MULTIQUO-VALUE-INDEX-027. Merge coordinado a develop y master sujeto a CI y staging.

## [1.5.1] - 2026-11-07 (pendiente de CI/staging)

- Genera PDF desde cualquier subQUO con índice de todas las alternativas guardadas y datos independientes por ruta, naviera y Ocean Freight; conserva el flujo individual histórico.
- Release DH-20261107-MULTIQUO-PERSISTED-PDF-026; sin autorización de despliegue a producción.

## [1.5.0] - 2026-11-07 (en validación)

- API FCL multiQUO: creación atómica por POE/naviera, consulta de grupo, selección comercial exclusiva y guardas ante aceptación errónea.
- Reutiliza la transacción de consecutivos/Outbox y conserva costos, rutas y tarifas por subQUO.
- Release DH-20261107-PRICING-MULTIQUO-025: staging pendiente, sin autorización de producción.


## [1.4.0] - 2026-11-06 (en desarrollo)

- Introduce las reglas de dominio para numeración estable de sub-QUO A..Z/AA, deduplicación por POE/naviera/haulage y prevalidación de selección exclusiva.
- Agrega pruebas unitarias del modelo. Pendientes: persistencia EF, API transaccional, integración con Dynamics, eventos realtime y documento consolidado.
- Release coordinado DH-20261106-MULTI-QUO-018; todavía no desplegado.


## [1.3.0] - 2026-11-06

- Permite guardar la ruta y los comentarios de la naviera como campos independientes y opcionales por cotización.
- Expone ambos campos en creación, edición, consulta y generación del PDF; agrega migración de columnas sin afectar cotizaciones previas.
- Release DH-20261106-CARRIER-ROUTE-COMMENTS-017, ecosistema Dhole 1.4.0.

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
