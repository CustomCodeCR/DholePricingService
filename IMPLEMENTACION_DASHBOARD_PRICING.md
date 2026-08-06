# Dashboard por rol: Pricing

## Backend

- Nuevo estado `Expired` para tarifas vencidas.
- Worker periódico `pricing.rate-expiration` que cambia automáticamente a `Expired` las tarifas cuya fecha `ValidTo` ya pasó.
- No reemplaza estados históricos terminales: `Closed`, `RejectedByManagement` y `RejectedByClient`.
- Configuración: `Pricing:RateExpiration:Enabled`.
- El worker utiliza la periodicidad general configurada en `Workers:Schedule` (actualmente 300 segundos y ejecución inmediata).
- Nuevo endpoint: `GET /api/pricing/rates/dashboard`.
- Filtros disponibles:
  - `createdFrom`, `createdTo`
  - `modifiedFrom`, `modifiedTo`
  - `validityFrom`, `validityTo`
- El resumen devuelve conteos por estado, actividad reciente y utilidad proyectada agrupada por moneda.
- La utilidad proyectada excluye tarifas pendientes, rechazadas, cerradas y vencidas.
- No se requiere migración: `RateStatus` ya se persiste como texto.

## Frontend

- Dashboard de Pricing visible para usuarios con scopes de Pricing y para superusuarios.
- Tarjetas de abiertas, aprobadas, rechazadas, solicitadas por cliente, cerradas y vencidas.
- Filtros por creación, modificación y vigencia.
- Resumen financiero por moneda y margen promedio.
- Tabla de tarifas con actividad reciente.
- Filtro y etiqueta de estado `Vencida` en la administración de tarifas.
- Edición bloqueada para tarifas cerradas o vencidas.

## Validación realizada

- Build de producción del frontend con Vite: correcto.
- `vue-tsc` conserva tres errores preexistentes y ajenos al dashboard en `PricingAiAnalysisDrawer.vue`, relacionados con contratos y método de análisis IA ausentes.
- No fue posible compilar ni ejecutar las pruebas de .NET porque el SDK `dotnet` no está instalado en el entorno de validación. Se añadieron pruebas unitarias para el vencimiento automático.
