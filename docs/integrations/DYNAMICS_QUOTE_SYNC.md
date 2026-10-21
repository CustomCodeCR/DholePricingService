# Pricing → Dynamics 365: Quote al aceptar la cotización

## Disparador y garantías

El POST al comando `crear_oferta` se prepara **únicamente** cuando la tarifa
cambia a `AcceptedByClient` y tiene `IdtraNumber` no vacío. La aprobación
interna de margen no genera una Quote. El trabajo queda persistido con la
revisión aceptada dentro de la misma transacción de Pricing y un worker
lo envía de forma independiente.

Se envía siempre la cabecera `Idempotency-Key` estable:
`dhole-pricing-quote-{rateId:N}-v{revisionNumber}`. Un reintento no usa
una clave diferente. Si el conector no confirma `succeeded` o devuelve
un estado ambiguo, el trabajo queda `NeedsAttention` para conciliación;
no se intenta una nueva creación con otra clave.

## Credencial de servidor

Configurar `DynamicsQuoteSync__ApiKey` **solo como secreto del backend de
Pricing API** mediante el gestor de secretos/entorno privado de despliegue.
Nunca incluirla en el repositorio, archivo de configuración versionado,
frontend, URL o prueba.

La clave publicada en una conversación debe revocarse y reemplazarse por
una clave nueva autorizada para la plantilla `crear_oferta` de la conexión
`principal`. El proceso deja los trabajos en cola si falta la clave.
Puede deshabilitarse explícitamente con `DynamicsQuoteSync__Enabled=false`.

El servicio requiere salida HTTPS a
`https://api.logisticacastrofallas.com/api/dynamics-connector/keyed/templates/crear_oferta`.
No usa ni guarda tokens OAuth de Dataverse.

## Mapeo de campos

| Campo Dynamics | Valor |
| --- | --- |
| `customeridtype` | `quote` |
| `name` | `RateName` o `RateCode` |
| `description` | Descripción a partir de código, cliente, modalidad, moneda e IDTRA |
| `effectivefrom`, `effectiveto` | `ValidFrom`, `ValidTo` |
| `new_equipo` | Valor real de Picklist obtenido del esquema, según equipo |
| `new_cantidadequipos` | Cantidad y tipo de los contenedores de la revisión |
| `new_categoriatarifa` | `100000001` |
| `new_preestado` | `100000003` |
| `new_proyecctoespecifico` | `100000004` |
| `new_tipodecliente` | `100000000` |
| `new_flete` | `TotalCostAmount` |
| `new_monto9` | `TotalCostAmount` |
| `msdyn_invoicesetuptotals` | `TotalSaleAmount` |
| `new_idtraasignado` | `IdtraNumber` |
| `new_pol`, `new_poe`, `new_pod` | Nombres de los puertos de la revisión |
| `new_tarifaincluye`, `new_tarifasujetaa`, `new_noincluye` | Incluye, sujeta a, excluye |
| `new_tiempodetransitoaprox` | Tiempo de tránsito |

El total de costo y el total de venta se consideran **en la moneda de la tarifa**.
No se aplica conversión implícita.

El comando publicado `crear_oferta` debe incluir todos los campos necesarios
(especialmente `customeridtype`, `new_equipo`,
`msdyn_invoicesetuptotals`, `new_flete`, `new_monto9` y las opciones fijas).
El worker obtiene su esquema y valida los valores Picklist antes del POST.
Si falta un campo u opción, no construye una Quote con información parcial.

## Diagnóstico

```sql
SELECT "RateId", "RevisionNumber", "Status", "Attempts",
       "LastError", "CreatedAtUtc", "CompletedAtUtc"
FROM pricing."DynamicsQuoteSyncJobs"
ORDER BY "CreatedAtUtc" DESC
LIMIT 50;
```

Estados: `Pending`, `Processing`, `Retry`, `Completed`, `NeedsAttention`.
Un trabajo `NeedsAttention` debe conciliarse en el conector de Dynamics
antes de cualquier intervención sobre su envío, para evitar duplicados.

## Validación previa a producción

1. Configurar clave nueva como secreto privado en el backend.
2. Confirmar que el comando activo tiene los campos indicados, los valores
   Picklist autorizados y que permite escribir `msdyn_invoicesetuptotals`.
3. Comprobar una cotización de prueba con IDTRA y equipamiento real.
4. Verificar en Dataverse el GUID y montos de la Quote creada.
5. Reintentar con la misma clave de idempotencia y verificar que no aparezca
   una segunda Quote.
