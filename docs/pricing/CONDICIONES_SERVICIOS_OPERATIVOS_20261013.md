# Matriz de costos condicionados para servicios distintos de Transporte Internacional

Referencia: cuatro fotografías aportadas el 13 de octubre de 2026. Estos importes son **tarifas indicadas en documentos**, no comprobantes de costo de proveedor ni precios finales aprobados para venta. No insertarlos automáticamente en `Costs`: los campos `CostAmount` y `SaleAmount` requieren aprobación de Pricing.

## Aplicación de los rubros

- Un costo condicionado **debe asociarse explícitamente** a uno o varios `pricing-services`. Si la tarifa solo contiene Transporte Internacional y no el servicio asociado, el rubro no debe incluirse. Si se contrató además Aduanas o Almacenaje Fiscal, se aplicará el costo de ese servicio.
- En `Costs`, filtrar por servicios, modalidad, ruta, agente, naviera e Incoterm antes de seleccionar el tramo. Usar IDs de Config; no comparar UUIDs escritos a mano ni textos de condiciones para identificar un contenedor.
- `CostType=Fixed` incorpora automáticamente el costo solamente cuando toda la condición coincide; `Optional` lo ofrece al usuario. No aplicar costos que no tengan tramo coincidente ni inventar un importe genérico.
- `ChargeBasis` se calcula después de resolver `CalculationMode`. Para `PerKg` y `PerExtraDay`, el monto resuelto **ya es el total del tramo**: usar `PerShipment` para no multiplicar dos veces.
- Mantener `currencyCode` real de cada rubro (USD o CRC) y el IVA como impuesto calculado sobre la venta según configuración fiscal, sin sumar el IVA de una cifra que ya lo incluye.
- Los días de almacenaje (`StorageDays`) son independientes de `FreeDays` y del tiempo de tránsito. Sin un dato de días fiscales positivo, **no se debe aplicar ningún cargo automático de almacenaje**, ni base ni diario.
- Para FCL mixto, resolver cargo por el contenedor de cada asignación (no asumir que todos tienen el `ContainerTypeId` primario) antes de cotizar varios equipos distintos.

## 1. Excedentes de combustible / bunker — USD

Servicio sugerido en Config: traslado terrestre Panamá–Costa Rica (seleccionar el `pricing-service` real; **no** depender del servicio marítimo/aéreo «Transporte Internacional»).

| Modalidad | Dimensión | Desde | Hasta | Tarifa documento | Cálculo |
|---|---|---:|---:|---:|---|
| Consolidado LTL | `WeightKg` | 1 | 500 | USD 90 | `Flat` |
| Consolidado LTL | `WeightKg` | 501 | 3000 | USD 200 | `Flat` |
| Consolidado LTL | `WeightKg` | 3001 | 5000 | USD 250 | `Flat` |
| Completo FTL | Según modalidad/equipo completo | — | — | USD 476 | `Flat` |

> Pesos de 5.001 kg en adelante no están cubiertos en la foto: solicitar tarifa, no inferir tramos.

## 2. Flete terrestre internacional Panamá → Costa Rica — USD

Asociar únicamente al servicio terrestre correspondiente, con filtro de ruta Panamá → Costa Rica; **no** agregar el flete si el cliente únicamente solicita agencia aduanal o almacenaje.

| Modalidad | Peso | Tarifa indicada | Modo |
|---|---|---|---|
| LTL consolidado | 1–500 kg | USD 180 | `Flat` |
| LTL consolidado | 501–3000 kg | USD 0.20 **por kg** + cargos especificados | `PerKg` |
| LTL consolidado | 3001 kg en adelante | USD 0.18 **por kg** + cargos especificados | `PerKg` |
| FTL completo | Contenedor completo | USD 1850 + gastos | `Flat` |

Gastos LTL impresos: rastreo USD 35, sellos USD 30, manejo documentación USD 90, Ducat USD 10; subtotal USD 165. Custodia USD 50 y marchamo USD 5 figuran por separado.
Gastos FTL impresos: rastreo USD 40, sellos USD 30, manejo USD 90, Ducat USD 10, custodia USD 100, marchamo USD 20; subtotal USD 290.
La anotación a mano parece añadir bunker USD 476 en FTL. **Confirmar si los gastos, bunker, custodia y marchamo se cobran por separado** antes de activar sus costos. No sumar dos veces conceptos que la tarifa base ya incluya.

## 3. Servicio de agencia aduanal

Asociar al servicio real «Trámite de Aduanas» o «Agencia Aduanal», sin exigir Transporte Internacional.

| Contexto | Importe fuente | Moneda | IVA |
|---|---:|---|---|
| Peso de 1 a 500 kg | 40,000 | CRC | adicional |
| Peso mayor de 500 hasta 1500 kg | 60,000 | CRC | adicional |
| Contenedor de 20 pies o consolidado | 175 | USD | adicional |
| Contenedor de 40 pies | 350 | USD | adicional |
| Permiso de importación | 15,000 | CRC | adicional |
| Examen previo | 15,000 | CRC | adicional |

La imagen impresa indica «de 500 a 1500», solapando el extremo 500; configurar el segundo tramo como `>500`. Reglas por peso y reglas por tipo de equipo **no son cargos acumulables de agencia**: elegir una sola tarifa principal según contexto, con precedencia de equipo cuando exista; permisos y examen son ítems opcionales independientes.

## 4. Almacenaje fiscal

Asociar al servicio «Almacenaje Fiscal» o al servicio equivalente en Config; excluir cotizaciones sin almacenaje fiscal.

### Consolidado: base para 1 a 5 días — CRC

| Bultos | Almacenaje | Manejo | Seguro | Otros/peonaje | Subtotal antes IVA | Columna adicional |
|---|---:|---:|---:|---:|---:|---:|
| 1–3 | 17,610.85 | 10,566.51 | 7,044.34 | 7,895.46 | 43,117.17 | 1,500 |
| 4–15 | 23,435.10 | 14,061.06 | 9,374.04 | 7,895.46 | 54,765.66 | 2,000 |
| 16–30 | 34,214.92 | 20,528.95 | 13,685.97 | 7,895.46 | 76,325.31 | 2,500 |
| 31–60 | 40,597.22 | 24,358.33 | 16,238.89 | 7,895.46 | 89,089.91 | 3,500 |
| 61+ | 54,018.11 | 32,410.87 | 21,607.24 | 7,895.46 | 115,931.68 | 4,500 |

La foto contiene montos de IVA y totales finales: **no usarlos como bases si Dhole calcula IVA en las líneas**. La columna `ADICIONAL` debe verificarse como valor **por día adicional** antes de crear reglas `PerExtraDay` con `IncludedStorageDays=5`. La nota de peonaje también cambia a partir de 500 bultos; aclarar su condición exacta antes de habilitar ese recargo.

### Contenedor completo: base hasta 7 días — USD

| Equipo | Base antes de IVA | Total con IVA impreso | Otros cobros por día adicional |
|---|---:|---:|---:|
| 20 pies | 275 | 310.75 | 10 + IVA |
| 40 pies | 375 | 423.75 | 20 + IVA |

Para las líneas de días adicionales, configurar `ContainerType` + `CalculationMode=PerExtraDay` + `IncludedStorageDays=7`, usando IDs de los tipos de contenedor equivalentes de Config. Contenedores especiales/Reefer no aparecen en el documento: no extrapolar.

## Reglas adicionales de aceptación

1. Aduanas sin Transporte Internacional: se presenta únicamente agenciamiento, permisos/examen opcionales y los servicios seleccionados.
2. Almacenaje sin Transporte Internacional: se presenta el rubro fiscal por bultos o contenedor; se cobran días excedentes solo si `StorageDays` fue indicado.
3. Panamá→Costa Rica sin transporte contratado: **no** insertar automáticamente el costo sintético de «Flete internacional terrestre».
4. LTL de 2,000 kg: el valor indicado del flete terrestre base es `2000 × 0.20 = USD 400`, **no** `2000 × (2000 × 0.20)`; costos internos y venta se configuran de forma independiente.
5. Dos servicios simultáneos: cada rubro se agrega una sola vez, no se duplica por cantidad de servicios coincidentes.
6. Cada tarifa que se apruebe requiere fuente, vigencia, moneda y confirmación de si es **costo**, **venta** o ambas.
