## [1.9.6] - 2026-11-10

- PATCH DH-20261110-PRICING-SELLER-MULTI-POE-063: el API de Pricing devuelve selectedPoeIds y selectedPoeNames en solicitudes de Ventas, en la bandeja de Pricing y en las vistas del vendedor. Valida que POE múltiples sean únicamente FCL, pertenezcan a la selección y no estén repetidos. La lista completa se incluye en las notificaciones de nuevas solicitudes y en la ruta mostrada a Pricing; los campos principales PoeId/PoeName y los formatos históricos siguen siendo compatibles. No se requiere migración de base de datos.

## [1.9.5] - 2026-11-10

- PATCH DH-20261110-PRICING-CRM-EXECUTIVE-059: compatibilidad validada del guard de identidad comercial existente: los payloads desde DholeWeb 1.17.3 incluyen executiveUserId del ejecutivo asignado en CRM. El middleware ya exige un vendedor Auth activo y conserva el caso especial Castro Fallas; no se debilitan permisos ni cambian contratos REST.
- Corrección funcional exclusiva de DholeWeb; versión de Pricing coordinada para el release global 1.17.3. Sin cambios en precios, persistencia o esquema.

## [1.9.4] - 2026-11-10

- PATCH DH-20261110-CRM-QUO-SUPERADMIN-058: solo los roles de superadministración pueden consultar candidatas, buscar el catálogo de QUO históricas, vincular cotizaciones y resincronizar sus datos CRM. Se exige el rol además de los scopes existentes; los demás usuarios reciben 403.
- Las consultas regulares por cuenta, las rutas, los KPI y el historial permanecen disponibles mediante los scopes previos. Pruebas de roles autorizados y denegados. Coordinado con DholeWeb 1.17.2.

## [1.9.3] - 2026-11-10

- PATCH DH-20261110-CRM-QUO-LIVE-057: GET /api/pricing/rates/crm-account/{accountId}/dashboard agrupa el estado oficial de todas las QUO vinculadas a esa cuenta, sin depender de oportunidades CRM ni límites de paginación.
- La reconciliación manual confirmada acepta hasta 1000 QUO por operación atómica; mantiene la protección contra cotizaciones de otra cuenta y preserva datos comerciales. Los listados por cuenta excluyen tarifarios que no son QUO.
- Coordinado con DholeWeb 1.17.1 / DholeSystem 1.17.1.

## [1.9.2] - 2026-11-09

- PATCH DH-20261109-CRM-QUO-LINK-SYNC-056: al vincular una QUO histórica, sincroniza la identidad CRM en RateHeader.ClientName, ExecutiveName y RateName, sin tocar importes, revisiones ni estado; conserva las identidades históricas de las revisiones publicadas.
- Endpoint idempotente para actualizar los datos de cliente de las QUO vinculadas previamente a la misma cuenta, sin reasignación a otra cuenta ni prospecto. Mismo evento de dominio e invalidación realtime.
- DholeWeb 1.16.1, ecosistema 1.16.2; requiere CI y validación en staging antes de master.

## [1.9.1] - 2026-11-09
- PATCH: add authenticated GET /api/pricing/rates/realtime-access and /api/pricing/costs/realtime-access with existing RateView/WorkspaceAccess scopes. No records or price data returned.
- Emit transactional metadata-only rate-list and cost-list invalidations for all rate header and cost mutations through Pricing Outbox. NotificationsHub delivers to authorized list observers and frontend re-queries REST. Staging infra now clones DholeSystem/develop.
- Ecosystem 1.16.1, master/production unchanged.

## [1.9.0] - 2026-11-09

- MINOR: endpoint `GET /api/pricing/rates/crm-legacy-catalog` para consultar todas las QUO históricas con búsqueda por código, cliente y ruta, paginación y estado de vinculación CRM.
- Conciliación manual confirmada a una cuenta CRM para registros con nombres históricos distintos o ausentes, sin reasignar cotizaciones ya vinculadas ni alterar importes, fechas, revisiones o estado comercial. Log estructurado por operación.
- Release coordinado `DH-20261109-CRM-LEGACY-QUO-CATALOG-055` con DholeWeb 1.16.0 y ecosistema 1.16.0. Pendiente CI y validación en staging.

## [1.8.1] - 2026-11-09

- PATCH: aceptar/rechazar cotizaciones Open del vendedor con una revisión publicada, sin solicitud de modificación activa ni aprobación de margen pendiente. Conservar importes de la última revisión enviada y no emitir PDF de un borrador.
- Registro idempotente y diferimiento explícito de la notificación de apertura cuando la revisión nueva sigue sin publicar. Release DH-20261109-SELLER-OPEN-DECISION-054, ecosistema 1.15.1.

## [1.8.0] - 2026-11-09

- Identificador de cuenta/prospecto CRM en cotizaciones nuevas y revisiones; conciliación manual de QUO legacy.

# Changelog

## [1.7.2] - 2026-11-09

- PATCH QUO-00000-000269: el endpoint comercial expone currentRateStatus desde la cabecera vigente, además del status histórico de la revisión publicada. Las solicitudes realmente abiertas siguen marcándose de forma independiente mediante pricingModificationActive.
- Conservar la última revisión comercial publicada y ocultar importes/costos/borradores no enviados; la nueva propiedad solo contiene el estado del workflow. Release DH-20261109-QUO269-SELLER-STATUS-053 con DholeWeb 1.14.4, ecosistema 1.14.4 (develop).

## [1.7.1] - 2026-11-09

- PATCH: la API comercial mantiene `revisionPending` para proteger revisiones no publicadas y expone `pricingModificationActive` solo cuando existe una solicitud de modificación realmente abierta en Pricing.
- Al completar la solicitud, el vendedor deja de ver trabajo activo de Pricing aunque siga pendiente la publicación; no se revelan borradores, costos internos ni información no enviada.
- Release coordinado DH-20261109-SELLER-PRICING-STATUS-052 con DholeWeb 1.14.3 y ecosistema 1.14.3.

## [1.7.0] - 2026-11-09 (NotificationsHub universal realtime pilot; candidate)

- Coordinación 2026-11-09: integrados los commits recientes de `develop` y `master` en la rama candidata, conservando los cambios del release 1.14.0; validación post-sincronización mediante CI.

- MINOR: retain existing domain Outbox/REST realtime producer under NotificationsHub-based universal delivery; no shared framework changes.
- Ecosystem candidate 1.14.0 / 16 Dhole repositories, release `rt-20261107-01` in isolated branch `feature/rt-notificationshub-20261109-01`.
- Staging deployment, operational audit and final integration remain pending. Production flags OFF.




## [1.6.7] - 2026-11-08 (Dhole realtime coordinated hardening)

- PATCH alignment to ecosystem 1.13.15 / rt-20261107-01; F2/F6 read-only AOF attestation and F10 branch retention hardening in DholeSystem.
- Candidate unchanged in functionality; all realtime flags remain OFF; F6/F7/F8 formal gates and F9/F10 promotion pending.





## [1.6.6] - 2026-11-08 (F9 candidate, F3 WSS review waiver)

- Incremento PATCH del release global 1.13.14 por política F9 de revisión de evidencia WSS ya aprobada en CI. No implica aprobar F3 staging, activar realtime ni desplegar. Sin cambios de negocio en este componente.






## [1.6.5] - 2026-11-08 (realtime F3, release candidato)

- Se sincroniza SemVer PATCH y la versión global Dhole 1.13.13 por el nuevo ensayo automático TLS/WSS con Traefik en DholeApiGateway. Sin cambios funcionales adicionales en este componente, sin merge ni despliegue.




## [1.6.4] - 2026-11-08 (F6/F9 staging-audit candidate)

- Sincroniza Dhole Realtime `rt-20261107-01` a versión global `1.13.12` tras incorporar auditoría real de persistencia en staging. PATCH 1.6.3 → 1.6.4; no modifica rutas, eventos, datos, contratos REST ni flags. No se fusiona hasta completar gates formales.





## [1.6.3] - 2026-11-08 (candidato F9, sin publicar)

- Sincroniza release global `rt-20261107-01` a `1.13.11` tras resolver los hallazgos de CI de Config, Reports y Gateway. Bump PATCH de DholePricingService `1.6.2` a `1.6.3`, sin modificar REST, eventos, precios ni datos; flags realtime OFF y PR F9 aún DRAFT.





## [1.6.2] - 2026-11-08 (candidato F9, sin publicación)

- Sincroniza metadatos de versión global del batch Dhole Realtime `rt-20261107-01` a `1.13.10`; actualización PATCH independiente de DholePricingService `1.6.1` → `1.6.2`. No cambia contratos REST, datos, lógica de negocio ni flags; requiere gates F0–F8 y CI de SHA final antes de merge.





## [1.6.1] - 2026-11-08 (candidato F9, sin publicación)

- Merge seguro de `develop` (650bfe0a203c) en la intermedia realtime: se conservan las 11 correcciones documentales 1.5.8–1.5.18 de Pricing y los 8 artefactos históricos de releases de FCL que faltaban; el código funcional REST no tenía cambios concurrentes.
- Se mantiene la implementación realtime F6.02 y su feature flag OFF. Versión PATCH 1.6.0 → 1.6.1; versión global candidata 1.13.9, sin modificar develop/master ni publicar.




## [1.6.0] - 2026-11-08 (candidato F6.02, no publicado)

- Añade invalidaciones mínimas para cambios de RateHeader al Outbox existente de Pricing y las envía a un stream separado dhole.pricing.realtime.v1; no sustituye cachés ni los eventos comerciales actuales.
- Permite productor realtime únicamente con los cuatro flags del release rt-20261107-01, apagados por defecto; los cambios y eventos se confirman dentro del mismo SaveChanges de EF.
- Mantiene REST autoritativo y registros de costos/cliente fuera del payload de invalidación; incorpora pruebas de contrato, seguridad, versionado y compilación de API y Worker.
- F6.02 sigue candidata en rama intermedia hasta completar adaptador Gateway, pruebas de entrega y matriz completa de mutaciones; no integra develop/master ni despliega.
- Versión del componente 1.5.7 -> 1.6.0; versión global propuesta 1.13.0.




## [1.5.18] - 2026-11-08

- Release coordinado `DH-20261108-PRICING-RATE-SELECTION-047` con DholeWeb 1.12.16: el asistente FCL vuelve a conservar el RateId elegido por POE/naviera y se bloquea por CI cualquier binding de estado eliminado durante las transformaciones Vite.
- Servicio de Pricing: solo versionamiento y metadatos de compatibilidad; los endpoints REST, el estado de importaciones, las tarifas, los precios y la persistencia permanecen intactos.
- Validar CI, smoke del wizard minificado y staging antes de producción.




## [1.5.17] - 2026-11-08

- Release coordinado `DH-20261108-PRICING-SETUP-TDZ-046` con DholeWeb 1.12.15: estabiliza «Crear tarifa» evitando la referencia anticipada a `form` en un watcher insertado por Vite.
- Solo metadatos; conserva contratos REST, lógica, tarifas y datos. CI y staging obligatorios.




## [1.5.16] - 2026-11-08

- Release coordinado `DH-20261108-PRICING-SETUP-TDZ-045` con DholeWeb 1.12.14 para corregir el error de inicialización Vue en «Crear tarifa».
- Sin cambios en endpoints de Pricing, persistencia, tarifas ni reglas de cálculo; mantiene compatibilidad con el frontend corregido.
- Requiere pruebas de montaje real en navegador y validación CI/staging antes de promover a master.




## [1.5.15] - 2026-11-08

- Release coordinado `DH-20261108-PRICING-RENDER-GUARD-044` con DholeWeb 1.12.13 para recuperar el renderizado del asistente FCL y eliminar referencias de cobertura tempranas en el frontend.
- Mantiene compatibilidad íntegra con los endpoints, importaciones, tarifas y RateId existentes. No modifica algoritmos, persistencia, ni datos de producción.
- Aplicar tras validación CI y staging y registrar versionado global del release.




## [1.5.14] - 2026-11-08

- Release coordinado `DH-20261108-PRICING-BLANK-043` con DholeWeb 1.12.12 para restaurar la pantalla «Crear tarifa» después del error de inicialización JavaScript de 1.12.11.
- Compatibilidad de las mismas consultas, importaciones y selecciones FCL con el frontend corregido, sin modificar datos, entidades, contratos REST ni algoritmos del servicio.
- Validación y despliegue de staging antes de integración definitiva a master.




## [1.5.13] - 2026-11-08

- Release coordinado `DH-20261108-FCL-OFFER-COVERAGE-042` con DholeWeb 1.12.11, corrigiendo la distinción entre ofertas FCL únicas y registros fuente.
- Los IDs de tarifa, precios y vigencias importadas continúan íntegros. Sin cambios de contratos REST ni esquema de datos.
- CI y despliegue staging requeridos antes de integración final a master.




## [1.5.12] - 2026-11-08

- Release coordinado con DholeWeb 1.12.10 para recuperar el renderizado estable de Pantalla 5 FCL (hotfix `DH-20261108-FCL-RESTORE-RENDER-041`).
- Sin cambios en contratos REST, persistencia ni valores de tarifas importadas. Mantiene compatibilidad API.
- Integración mediante ramas intermedias y validación CI/staging obligatoria antes de producción.




## [1.5.11] - 2026-11-08

- Release coordinado `DH-20261108-FCL-BLANK-SCREEN-040` para resolver la pantalla negra al cargar 110 tarifas FCL en DholeWeb 1.12.9.
- Contrato de Pricing sin cambios; los registros de importación, correos y tarifas no se modifican.
- CI y staging obligatorios antes de cualquier integración a master y producción.




## [1.5.10] - 2026-11-08

- Release coordinado `DH-20261108-FCL-CARRIER-CARDS-SELECTION-039` con DholeWeb 1.12.8 para la agrupación real de tarifas FCL por naviera y POE y selección por RateId.
- No altera el contrato REST ni las tarifas persistidas, reaprovisiona la versión de Pricing y conserva todos los registros de origen.
- CI y staging requeridos antes de integración a master y producción.




## [1.5.9] - 2026-11-08

- Release coordinado con DholeWeb 1.12.7 para agrupación visual de tarifas FCL equivalentes.
- Conserva contratos REST, tarifas persistidas y los ID originales de origen, sin migraciones ni eliminación de datos.
- Release `DH-20261108-FCL-VISUAL-GROUPS-038`, CI/staging obligatorios antes de producción.




## [1.5.8] - 2026-11-08

- Release coordinado con DholeWeb v1.12.5 para consolidación FCL por identidades canónicas en la pantalla 5.
- Conserva contratos REST, registros importados e identificadores de tarifas existentes; no requiere migraciones de datos.
- Release `DH-20261108-FCL-VISIBLE-DEDUP-036`. CI y staging obligatorios antes de master y producción no autorizada.




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


## [1.5.20] - 2026-11-09

- MultiQUO establece UseAllInPresentation por alternativa FCL Panamá → Costa Rica en el servidor; no hereda el estado ALL IN de otro POE.
- Mantiene líneas financieras internas para detalle y PDF, sin alterar cálculos ni los contratos REST.
- Release coordinado DH-20261109-PRICING-FCL-PANAMA-ALLIN-049 con DholeWeb 1.12.18. Requiere CI y staging antes de master.



## [1.5.19] - 2026-11-09

- Endpoint opt-in `groupQuotes=true` para Gestión de tarifas: la MultiQUO entrega una única fila representativa (la seleccionada, o el ancla inicial), con conteo, paginación y número principal correctos.
- Conserva cada subcotización y su ID original, el acceso a los detalles y la compatibilidad de los demás consumidores REST.
- Release coordinado DH-20261109-PRICING-QUO-PORT-ISOLATION-048 con DholeWeb 1.12.17, que aísla el POE y los campos marítimos manuales por alternativa.
- Requiere compilación, pruebas y validación de staging antes de promover a master.
