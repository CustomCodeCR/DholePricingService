# Pricing → Dynamics 365: sincronización de QUO por RateId

## Cuándo se envía

Pricing encola una revisión de una tarifa cuando **ya está aceptada por el cliente
(`AcceptedByClient`) y tiene uno o más IDTRA**:

- al aceptar la tarifa con IDTRA asignado;
- al asignar el IDTRA después de aceptarla;
- al guardar una nueva revisión de una tarifa que permanece aceptada con IDTRA.

La aprobación interna por margen, por sí sola, **no crea una QUO en Dynamics**.
El cambio de estado y la cola de sincronización se guardan en la misma
transacción. Cada revisión se identifica por `RateId` y
`RevisionNumber`, para evitar trabajos duplicados.

El backend usa exclusivamente:

`POST /api/dynamics-connector/keyed/templates/crear_oferta/execute-with-total`

Con `X-Dhole-Api-Key` y la clave estable por revisión:

`Idempotency-Key: dhole-pricing-quote-{rateId:N}-v{revisionNumber}`

No usar `/execute` desde Pricing: ese endpoint no garantiza la creación ni
actualización de la línea de venta. El conector `execute-with-total`
administra la cabecera y la línea y valida las autorizaciones previamente.

## Identidad y creación/modificación

El payload incluye siempre `connectionCode = principal`,
`dholeRateId = RateHeader.Id` (GUID real), `totalSaleAmount`,
`totalCostAmount` e `inputs`, obtenidos de la revisión aceptada.

La plantilla activa `crear_oferta` puede ser `create` o `upsert`.
Para **crear y modificar la MISMA oferta** en Dynamics, publicar la
plantilla como **upsert** y aprobar su clave alternativa única
`cr933_dholerateid` (o el campo heredado `new_idtraasignado` si sigue
siendo la clave registrada). El conector construye `keys` de forma confiable
a partir del GUID `dholeRateId`; Pricing no necesita enviar `keys`
separadamente a `execute-with-total`. Ningún IDTRA puede convertirse en
clave alternativa.

Si la plantilla está en `create`, cada revisión puede crear otra oferta:
no es apropiado para modificar una QUO existente. Tampoco se debe modificar
o recrear a ciegas una QUO histórica creada antes de registrar la clave
alternativa; el conector exige conciliación antes de continuar.

## Mapeo comercial

| Campo de Dynamics | Origen en Pricing |
| --- | --- |
| `new_flete` | `TotalSaleAmount` (venta TOTAL, no flete del transportista) |
| `new_monto1` | `TotalCostAmount` (costo TOTAL de proveedor) |
| `new_monto2` … `new_monto9` | `0` numérico |
| `new_equipo` | Equipo seleccionado: opción real del esquema activo de Dynamics |
| `new_cantidadequipos` | Cantidad/tipo de equipos, o cantidad facturable en LCL/LTL |
| `new_idtra1` … `new_idtra10` | IDTRA asignados, uno por campo lookup disponible, en orden |
| `cr933_dholerateid` | GUID permanente `RateHeader.Id`, cuando forma parte de la plantilla |
| `new_idtraasignado` | GUID permanente `RateHeader.Id` SOLO para plantillas heredadas que lo emplean como identidad |
| `name`, `description` | Nombre o número QUO, información de la tarifa y los IDTRA |
| `effectivefrom`, `effectiveto` | Fechas de validez de la tarifa |
| `new_pol`, `new_poe`, `new_pod` | Origen, entrada y destino almacenados |
| `new_categoriatarifa` | Opción aprobada, preferencia `100000001` |
| `new_preestado` | Opción aprobada, preferencia `100000003` |
| `new_proyecctoespecifico` | Opción aprobada, preferencia `100000004` |
| `new_tipodecliente` | Opción aprobada, preferencia `100000000` |
| `transactioncurrencyid` | GUID obtenido en Dynamics para ISO `USD` o `CRC` |
| `msdyn_invoicesetuptotals` | Venta total, solo si está publicado en la plantilla |

El conector reafirma los nueve montos y `new_flete` desde los importes
confiables del nivel superior; no acepta valores financieros arbitrarios
del template. La moneda corresponde a la tarifa; no se convierte tácitamente.

Los IDTRA se normalizan al prefijo `IDTRA999.`, sin anexos `// QUO`;
se eliminan duplicados y se valida un máximo de diez. Los campos IDTRA
no utilizados se omiten si son opcionales; nunca se inventan referencias.
Un número mayor de IDTRA que de lookups disponibles produce un error
explícito antes de escribir. `cr933_dholerateid` nunca contiene IDTRA.

## Línea de venta

`execute-with-total` crea **una** línea administrada por Dhole
(`quotedetailname = Total venta de tarifa Dhole`) con cantidad `1` y
precio unitario `TotalSaleAmount`. Dataverse calcula su `totalamount`.
En `upsert`, el conector conserva la misma QUO y actualiza esa línea
existente; si detecta líneas ajenas o un estado ambiguo, exige conciliación
en vez de duplicar datos.

## Clave y autorización

La API key del conector se lee en el backend de Pricing a través de
`DynamicsQuoteSync__ApiKey` en `/opt/dhole/.env`. No debe incorporarse al
repositorio, frontend ni registros. Debe corresponder a la **versión
activa** de `principal/crear_oferta`, con permisos de cabecera, búsqueda
de clientes/IDTRA/moneda y lectura/escritura de quotedetail. Para
`upsert` también se requieren los grants del alternate key y
actualización de cabecera y línea.

`/write-readiness` y `/schema` permiten comprobar la autorización
sin escribir en Dataverse. HTTP 401 implica clave no válida para la versión
activa; HTTP 403/409 puede señalar grants, autorización de escrituras o
clave alternativa no registrada. No regenerar idempotencia ni hacer POST
manual como respuesta automática a esos errores.

## Diagnóstico y verificación

```sql
SELECT "RateId", "RevisionNumber", "Status", "Attempts",
       "LastError", "CreatedAtUtc", "CompletedAtUtc"
FROM pricing."DynamicsQuoteSyncJobs"
ORDER BY "CreatedAtUtc" DESC
LIMIT 50;
```

Estados de la cola: `Pending`, `Processing`, `Retry`,
`Completed`, `NeedsAttention`. Antes de reintentar una operación con
resultado ambiguo, conciliar cabecera y línea en Dynamics. Probar:
alta aceptada + IDTRA, IDTRA posterior, segunda revisión aceptada,
repetición con la misma idempotencia, 10 IDTRA y una tarifa con
venta/costo diferentes. Confirmar que hay una QUO y una línea por RateId,
y que los montos se actualizan, sin crear duplicados.
