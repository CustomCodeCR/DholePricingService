# DH-386 · Fuente definitiva de costos para el asistente de cotizaciones

El único endpoint de lectura de costos del asistente de creación y modificación de QUO es:

`GET /api/pricing/costs/wizard-bundle`

La respuesta sigue el contrato REST existente:

- `data.catalog`: catálogo completo, **solo de referencia** para mostrar metadatos de cargos ya contratados y coincidencias históricas por `CostId`. Nunca autoriza crear nuevas líneas.
- `data.applicable`: único conjunto de costos autorizados para el contexto completo de modalidad, POL, POE, POD, naviera/agente, Incoterm y servicios, con `containerTypeId`, `weightKg`, `packages` y `storageDays` cuando existen.
- Edición: `RateDetails` persistidos son la verdad de la revisión abierta. No reemplazar ni anexar líneas automáticamente. Solo una acción explícita de actualización puede consultar el bundle y cambiar precios de líneas del mismo `CostId`; no recuperar cargos previamente eliminados ni anexar opcionales sin selección.
- FCL multimodal: el `Freight` del transporte marítimo por equipo y un `Freight` terrestre con `CostId` propio son conceptos distintos. No eliminar el terrestre durante la normalización FCL de fletes por contenedor. Debe mostrarse en el bloque **Flete Internacional**, nunca ocultarse como cargo de destino.
- Un resultado vacío o un fallo de red no debe producir líneas de costo cero ni eliminar valores editados; conservar el estado y mostrar un error.

## Ejemplo de regresión real

El contexto observado para Balboa, Panamá / naviera MAERSK devolvió el costo `ae17c229-3d18-48a2-a22d-61f23264780d`, nombre `Flete Internacional Terrestre`, `costDetailType=Freight`, `chargeBasis=PerContainer`, costo/venta USD 2140/2140, modalidades FCL y FTL. Este `CostId` debe mantenerse independiente del flete marítimo 40HC de la misma alternativa.

## Alcance

Esta actualización **no cambia** el contrato del endpoint, el funcionamiento de SignalR, el cálculo del backend ni los datos de costos. DholeWeb utiliza exclusivamente `wizard-bundle` para obtener cargos; el resto de servicios del ecosistema conservan sus endpoints REST existentes. CI, staging y verificación de QUO persistida siguen siendo obligatorios antes de integrar a producción.
