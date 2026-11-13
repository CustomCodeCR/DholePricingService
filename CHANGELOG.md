## [1.18.2] - 2026-11-13

- PATCH DH-20261113-PRICING-OWN-LCL-141: CFS del consolidado LCL propio se guarda y actualiza por CBM cobrable, con mínimo 1; nunca se reemplaza por 0,118 CBM físico. Costo/venta se recalculan a USD 8,00 para una unidad con precio USD 8,00/CBM.
- Cotizaciones históricas usan la proyección comercial de CBM cobrable en API, PDF y totales sin alterar las revisiones publicadas. Coloader, CFT Miami y otros cargos se mantienen independientes.
- Pricing 1.18.1 → 1.18.2; Web/ecosistema 1.51.2 → 1.51.3. Requiere CI/staging antes de master.

## [1.18.1] - 2026-11-13

- PATCH DH-20261113-PRICING-OWN-LCL-139: CFS de origen FCA/EXW usa CBM cobrable (incluido el mínimo comercial), no volumen físico. Se mantiene la base por CBM cobrable desde la matriz hacia el asistente.
- CR_HANDLING (Manejos) y CR_ZONE conservan el costo editable al guardar, consultar y calcular la cotización: se elimina la normalización silenciosa a USD 0. Solo los costos/ventas configurados en LCL propios son fuente de los cargos propios; sin modificar las tarifas coloader.
- Pricing 1.18.0 → 1.18.1; Web 1.51.0 → 1.51.1; ecosistema 1.51.1. CI y staging requeridos antes de master; sin autorización de despliegue a producción.

## [1.18.0] - 2026-11-12

- MINOR DH-20261112-PRICING-ROUTE-124: nuevo PATCH /api/pricing/rate-requests/{id}/pricing-route, exclusivo del scope de actualización de tarifas y con reserva vigente. Permite modificar únicamente POL, POE y POD; rechaza solicitudes cerradas.
- Persiste el cambio de ruta efectiva sin reescribir el formulario, Incoterm, carga, ni los servicios originales de Ventas. Conserva la ruta inicial del vendedor y el historial de ajustes de Pricing en PayloadJson, sin cambiar SLA, prioridad, estados ni los contratos REST anteriores.
- Pruebas unitarias de inmutabilidad de Ventas, historial y bloqueo de solicitudes finalizadas. PricingService 1.17.0 → 1.18.0; Web 1.44.2 → 1.45.0; ecosistema 1.44.2 → 1.45.0. CI y staging pendientes; sin despliegue a producción.

## [1.17.0] - 2026-11-12

- MINOR DH-20261112-PRICING-TARIFF-CRM-121: al aplicar un tarifario maestro a una cuenta/prospecto CRM se valida la identidad por ID y los scopes/ACL efectivos consultando GET /api/crm/pricing-client-options con el token de sesión. Nombres y ejecutivo canónicos son resueltos en el servidor (incluido Castro Fallas).
- La QUO derivada guarda ClientAccountId o ClientLeadId, conserva íntegros costos, ruta, revisión, vigencia y estado Abierta, sin alterar el maestro. El contrato de duplicación acepta nuevos IDs opcionales y mantiene compatibilidad con las llamadas REST antiguas.
- Pruebas de integración con un CRM simulado para vendedor institucional, cuenta, permisos insuficientes y discrepancias de ID. Pricing 1.16.3 → 1.17.0, Web 1.43.6 → 1.44.0 y ecosistema 1.43.8 → 1.44.0; pendiente CI, staging y producción autorizada.

## [1.16.3] - 2026-11-12

- PATCH DH-20261112-DYNAMICS-QUO-613-619-118: recuperación controlada de QUO-00000-000619 solo tras rechazo 4xx probado o conciliación administrativa explícita de la cabecera como no aplicada; nunca interpreta un 409 o no_record_id como ausencia.
- Verificación idempotente de la única línea de venta reconocida Dhole de QUO-00000-000613 contra la venta, el RateId y el encabezado existente, sin repetir POST de cabecera ni de línea.
- Pricing 1.16.2 → 1.16.3, Dynamics Connector 1.4.0 → 1.4.1, ecosistema 1.43.6. Sin cambios incompatibles REST, SignalR ni Outbox. CI, staging y consulta real de Dataverse pendientes.

## [1.16.2] - 2026-11-12

- PATCH DH-20261112-PRICING-OUTBOX-CORRELATION-115: corregido SQLSTATE 22001 por correlation_id de Outbox (varchar(100)) al reenviar notificaciones de QUO aceptadas; incidente QUO-00000-000609.
- Clave manual compacta y determinista, preservando tipo, tarifa, revisión y requestId. Normalización SHA-256 de claves largas en IntegrationEventOutboxWriter para que la correlación del registro de Outbox y el idempotencyKey del evento sean exactamente iguales; solución transversal compatible con futuros servicios de Pricing.
- Sin cambios de esquema, contratos REST, cola Redis/SignalR ni framework. Pruebas contra el límite físico de 100 caracteres, idempotencia y tipos de notificación.
- DholePricingService 1.16.1 → 1.16.2; Web 1.43.2 → 1.43.3; System/ecosistema 1.43.3 → 1.43.4. CI/staging/SMTP pendientes. Despliegue de producción solo con autorización expresa.

## [1.16.1] - 2026-11-12

- PATCH DH-20261112-DYNAMICS-IDTRA-FIRST10-117: el mapeo de QUO hacia Dynamics acepta listas de más de 10 IDTRA, normaliza y deduplica manteniendo el orden y solo envía las primeras 10 referencias a new_idtra1..10 y a la descripción de Dynamics.
- Conserva todos los IDTRA en el snapshot, el historial y la QUO de Pricing; no cambia montos, RateId, revisión, idempotencia ni el control de lookups faltantes. Incluye regresión con los 23 IDTRA de QUO-00000-000552.
- DholePricingService 1.16.0 → 1.16.1; ecosistema Dhole 1.43.2 → 1.43.3 (master previo 1.43.1). CI y staging pendientes; producción sin autorización. REST, Outbox y SignalR existentes sin cambios.

## [1.16.0] - 2026-11-12

- MINOR DH-20261112-PRICING-RESEND-CHOICE-114: el reenvío manual de QUO aceptadas permite escoger notificación de Apertura con PDF y respaldos, correo de cotización enviada según CRM o aviso preliminar de aceptación SIN PDF.
- Corrige el fallo genérico de reenvío (QUO-00000-000609) diferenciando etapas Reports/PDF, adjuntos y Outbox con códigos de error explícitos. La alternativa sin PDF es voluntaria, está identificada como incompleta y no autoriza abrir trámite sin los documentos necesarios.
- Endpoints existentes conservados; /acceptance-email/resend acepta notificationType en allowlist e idempotencia por requestId. Los destinatarios de cotización se validan mediante el resolver CRM existente; no permite emails arbitrarios.
- DholePricingService 1.15.0 → 1.16.0; DholeWeb 1.42.0 → 1.43.0; ecosistema Dhole 1.43.0. Compatible con REST/SignalR/Outbox; no modifica el framework, historial comercial ni IDTRA.
- CI, staging y entrega SMTP deben verificarse antes de declarar completado el incidente.

## [1.15.0] - 2026-11-12

- MINOR DH-20261112-PRICING-ACCEPTANCE-MAIL-113: aceptación desde Pricing incluye a pricingcf@castrofallas.com y al vendedor en CC para que Apertura y el equipo comercial reciban visibilidad consistente.
- Cuando falla el PDF o la preparación del Outbox, el API conserva la aceptación y responde 202 con aviso; se agrega reintento controlado e idempotente para QUO ya aceptadas, incluida QUO-00000-000609.
- Incluye POST /api/pricing/rates/{rateId}/acceptance-email/resend para reenvíos manuales intencionales, idempotentes por operación, confirmados mediante DhModal en Web.
- Componente DholePricingService 1.14.0 → 1.15.0 y ecosistema Dhole 1.42.0. Sin cambio de esquema ni framework; REST, SignalR y Outbox vigentes. CI/staging pendientes; sin despliegue de producción.

## [1.14.0] - 2026-11-11

- MINOR DH-20261111-PRICING-QUO-LINKED-104: nuevo estado comercial QUOVinculada para cotizaciones cuya operación fue aceptada bajo otra QUO del mismo cliente. El vínculo a la QUO aceptada es obligatorio, persistente y validado en Pricing.
- El módulo Web muestra «Cliente aceptó otra QUO» con búsqueda, selección y navegación al documento aceptado; las tarifas vinculadas se muestran por separado y no computan como vencidas o no aceptadas.
- Componente DholePricingService 1.13.0 → 1.14.0, ecosistema Dhole 1.37.0. Contratos REST, SignalR e infraestructura Outbox preservados. CI/staging pendientes; sin autorización de producción.

## [1.13.0] - 2026-11-11

- MINOR DH-20261111-CRM-QUOTE-MULTI-CONTACTS-102: varios contactos de cuenta seleccionados para todas las cotizaciones futuras, con copia al vendedor asignado. Validación CRM previa al Outbox de cada correo; envío individual por contacto sin revelar otros destinatarios.
- Compatible con REST y arquitectura realtime existente. Ecosistema global 1.35.0. CI y staging pendientes.

## [1.12.0] - 2026-11-11

- MINOR DH-20261111-CRM-ACCOUNT-QUOTE-DELIVERY-093: destinatarios permanentes por cuenta para todas las cotizaciones futuras, con validación en CRM antes del Outbox.
- Solo al vendedor, o al contacto elegido con copia al vendedor. REST, realtime y procesamiento idempotente existentes.
- Ecosistema 1.33.0; CI y staging pendientes.

## [1.11.3] - 2026-11-11

- PATCH DH-20261111-PRICING-SELLER-REQUEST-HISTORY-093: GET /api/pricing/rate-requests/by-rate/{rateId}/history de solo lectura, que devuelve la solicitud original y las modificaciones vinculadas a una QUO sin alterar payloads, estados ni revisiones existentes.
- Compatible con pricing.rate.view, coordinado con Web 1.32.2 y ecosistema 1.32.2. CI/staging pendientes. Producción no autorizada.

## [1.11.2] - 2026-11-11

- PATCH DH-20261111-PRICING-NONFCL-DUPLICATE-091: copias editables de LCL, FTL, LTL, aéreo y aéreo consolidado toleran marcadores internos de proveedor, catálogos de equipo terrestre y referencias históricas no-FCL; FCL mantiene validación estricta de navieras y contenedores marítimos.
- Copia ruta, carga, servicios, volumen cobrable, almacenamiento e impuestos; sin cambios de REST, SignalR, migraciones ni framework. CI y staging pendientes, sin autorización de producción.

## [1.11.1] - 2026-11-11

- PATCH DH-20261111-CRM-QUOTE-SELLER-ACCESS-090: permite que el vendedor autorizado de una cuenta CRM envíe una QUO creada por otro usuario de Pricing, sin quedar bloqueado por el middleware de propiedad de solicitudes.
- Excepción restringida a PATCH /api/pricing/rates/{id}/status y a scopes crm.quote.read + crm.account.update; Pricing verifica vínculo a cuenta, visibilidad CRM, contacto y vendedor antes de confirmar la transición y el Outbox. No amplía permisos generales de Pricing.
- Release correctivo de 1.11.0, coordinado con DholeSystem 1.31.1. CI/staging pendientes y producción no autorizada.

## [1.11.0] - 2026-11-11

- MINOR DH-20261111-CRM-QUOTE-DELIVERY-089: permite enviar las cotizaciones vinculadas a cuentas CRM al vendedor asignado o a un contacto activo seleccionado, con copia exclusiva al vendedor asignado.
- Resuelve contactos y ejecutivo en CRM con permisos del solicitante antes de crear el Outbox; el PDF comercial se adjunta y no se exponen enlaces privados de Dhole al cliente. Conserva el flujo anterior y la infraestructura de notificaciones idempotente.
- Release coordinado con DholeWeb 1.31.0 y DholeSystem 1.31.0; CI/staging pendientes; producción no autorizada.

## [1.10.1] - 2026-11-11

- PATCH DH-20261111-PRICING-OPENING-ATTACHMENTS-084: backport de develop para que cada apertura adjunte PDF comercial, soportes de todas las solicitudes relacionadas y documentos finales de la cotización, deduplicados por ID de Storage.
- El contenido se captura en el Outbox, con controles de 30 archivos/14 MiB e indicación de documentos no disponibles; coordinado con Notifications 1.3.1 y System 1.25.2. CI y validación de staging obligatorios antes de promover master.

## [1.10.0] - 2026-11-10

- MINOR DH-20261110-CRM-LEAD-QUO-PARITY-082: Dashboard oficial de QUO por ClientLeadId y reconciliación manual de QUO históricas a prospectos, con confirmación superadministrada. No permite reasignar cotizaciones de cuentas/prospectos distintos, no modifica montos ni estados, sincroniza solicitudes por RateId.
- Conserva el release de tarifas vencidas 1.9.12; contratos REST existentes y realtime compatibles. CI y staging verificados para el batch previo de develop.

## [1.9.12] - 2026-11-10

- PATCH DH-20261110-SELLER-EXPIRED-081: permite que el vendedor asignado acepte o rechace cotizaciones vencidas mediante el endpoint de decisión ya existente. Conserva fecha de vigencia, auditoría, idempotencia y confirmaciones de apertura; requiere motivo para rechazo.
- Solicitar modificación ya era válido con Expired; no cambia la seguridad ni contratos REST/SignalR.

## [1.9.11] - 2026-11-10

- PATCH DH-20261110-CRM-HIDE-TARIFARIO-067: CRM > Cuentas > Cotizaciones > pendientes excluye antes de contar y paginar las QUO cuyo cliente histórico contenga «TARIFARIO», sin distinguir mayúsculas. La búsqueda, el total y la selección masiva reciben resultados filtrados desde Pricing.
- No se eliminan ni modifican tarifas, cotizaciones vinculadas, importes o estados. Permisos y contratos REST conservados.

## [1.9.10] - 2026-11-10

- PATCH DH-20261110-PRICING-CRM-REQUEST-IDENTITY-067: la fuente de modificación devuelve el identificador CRM, nombre del cliente y ejecutivo actuales de la cotización sin reintroducir datos antiguos de la solicitud original. Cotizaciones históricas editables; sin cambios de esquema ni framework. Release global Dhole 1.17.12, CI y staging pendientes.

## [1.9.9] - 2026-11-10

- PATCH DH-20261110-PRICING-REQUEST-CURRENT-CLIENT-066: las solicitudes asociadas muestran el cliente y ejecutivo vigentes de su tarifa en bandejas, dashboard y detalle, incluso si se crearon antes de la migración CRM. Los cambios de cliente realizados desde el wizard se guardan junto con las solicitudes vinculadas en el mismo SaveChanges. Se preserva el historial original de modificaciones, estados, SLA, revisiones e importes.

## [1.9.8] - 2026-11-10

- PATCH DH-20261110-CRM-QUO-MODIFICATION-REQUEST-SYNC-065: al vincular o resincronizar una QUO con una cuenta CRM, actualizar en la misma transacción el cliente y ejecutivo de todas sus solicitudes asociadas, incluidas las modificaciones abiertas e históricas; respetar diferencias solicitadas, payload original, estados, revisiones y SLA. Sin modificaciones al framework ni al esquema de datos.

## [1.9.7] - 2026-11-10

- FIX DH-20261110-PRICING-SELLER-MULTI-POE-064: completar la importación explícita de MSTest en las pruebas de regresión de POE múltiples FCL, manteniendo el contrato de la versión 1.9.6 sin cambios de esquema ni de comportamiento de API.

## [1.9.6] - 2026-11-10

- PATCH DH-20261110-PRICING-SELLER-MULTI-POE-063: el API de Pricing devuelve selectedPoeIds y selectedPoeNames en solicitudes de Ventas, en la bandeja de Pricing y en las vistas del vendedor. Valida que POE múltiples sean únicamente FCL, pertenezcan a la selección y no estén repetidos. La lista completa se incluye en las notificaciones de nuevas solicitudes y en la ruta mostrada a Pricing; los campos principales PoeId/PoeName y los formatos históricos siguen siendo compatibles. No se requiere migración de base de datos.

## [1.9.5] - 2026-11-10

- PATCH DH-20261110-PRICING-CRM-EXECUTIVE-059: compatibilidad validada del guard de identidad comercial existente: los payloads desde DholeWeb 1.17.3 incluyen executiveUserId del ejecutivo asignado en CRM. El middleware ya exige un vendedor Auth activo y conserva el caso especial Castro Fallas; no se debilitan permisos ni cambian contratos REST.
- Corrección funcional exclusiva de DholeWeb; versión de Pricing coordinada para el release global 1.17.3. Sin cambios en precios, persistencia o esquema.

## [1.9.4] - 2026-11-10

- PATCH DH-20261110-CRM-QUO-SUPERADMIN-058: rol de superadministración obligatorio en el catálogo de QUO históricas, búsqueda de candidatas, conciliación de vínculos y sincronización de cliente; se preservan los scopes anteriores.
- Acceso de usuarios ordinarios a cotizaciones por cuenta, KPI e historial continúa sin cambios. Pruebas de autorización por roles y rechazo a administradores estándar. Coordinado con DholeWeb 1.17.2.

## [1.9.3] - 2026-11-10

- PATCH DH-20261110-CRM-QUO-LIVE-057: GET /api/pricing/rates/crm-account/{accountId}/dashboard agrupa el estado oficial de todas las QUO vinculadas a esa cuenta, sin depender de oportunidades CRM ni límites de paginación.
- La reconciliación manual confirmada acepta hasta 1000 QUO por operación atómica; mantiene la protección contra cotizaciones de otra cuenta y preserva datos comerciales. Los listados por cuenta excluyen tarifarios que no son QUO.
- Coordinado con DholeWeb 1.17.1 / DholeSystem 1.17.1.

## [1.9.2] - 2026-11-09

- PATCH DH-20261109-CRM-QUO-LINK-SYNC-056: asociación histórica QUO ahora actualiza RateHeader.ClientName, ExecutiveName y RateName desde CRM, conserva importes, revisiones y estados y emite evento de dominio.
- Endpoint para sincronizar tarifas previamente vinculadas, idempotente y con protección contra reasignación. Release coordinado Web 1.16.1, ecosistema 1.16.2; previa validación de staging.

## [1.9.0] - 2026-11-09

- MINOR: Catálogo completo server-side de QUO históricas vía GET /api/pricing/rates/crm-legacy-catalog y reconciliación manual confirmada con cuenta CRM.
- Solo se pueden asociar QUO sin cuenta/prospecto; preserva el histórico comercial, datos financieros y las APIs REST existentes. Registro de auditoría.
- Release DH-20261109-CRM-LEGACY-QUO-CATALOG-055 con DholeWeb 1.16.0 y ecosistema 1.16.0.

## [1.6.0] - 2026-11-09

- Asociación y migración manual de cotizaciones antiguas a cuentas CRM con UUID sin alterar valores.

# Changelog

## [1.5.22] - 2026-11-09

- PATCH QUO-00000-000269: el endpoint comercial expone currentRateStatus desde la cabecera vigente, además del status histórico de la revisión publicada. Las solicitudes realmente abiertas siguen marcándose de forma independiente mediante pricingModificationActive.
- Conservar la última revisión comercial publicada y ocultar importes/costos/borradores no enviados; la nueva propiedad solo contiene el estado del workflow. Release DH-20261109-QUO269-SELLER-STATUS-053 con DholeWeb 1.12.22, ecosistema 1.12.22 (master).

## [1.5.21] - 2026-11-09

- PATCH: endpoint de tarifas del vendedor distingue la revisión comercial sin publicar (`revisionPending`) de una solicitud de modificación que permanece abierta (`pricingModificationActive`).
- No cambia los estados de envío, el PDF ni la autorización; preserva la última cotización enviada y oculta la revisión nueva hasta su publicación.
- Release DH-20261109-SELLER-PRICING-STATUS-052 (master candidate); DholeWeb 1.12.21 / ecosistema 1.12.21.

## [1.5.20] - 2026-11-09

- MultiQUO establece UseAllInPresentation por alternativa FCL Panamá → Costa Rica en el servidor; no hereda el estado ALL IN de otro POE.
- Mantiene líneas financieras internas para detalle y PDF, sin alterar cálculos ni los contratos REST.
- Release coordinado DH-20261109-PRICING-FCL-PANAMA-ALLIN-049 con DholeWeb 1.12.18. Requiere CI y staging antes de master.

## [1.5.19] - 2026-11-09

- Endpoint opt-in `groupQuotes=true` para Gestión de tarifas: la MultiQUO entrega una única fila representativa (la seleccionada, o el ancla inicial), con conteo, paginación y número principal correctos.
- Conserva cada subcotización y su ID original, el acceso a los detalles y la compatibilidad de los demás consumidores REST.
- Release coordinado DH-20261109-PRICING-QUO-PORT-ISOLATION-048 con DholeWeb 1.12.17, que aísla el POE y los campos marítimos manuales por alternativa.
- Requiere compilación, pruebas y validación de staging antes de promover a master.

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
