# Revisión manual de importaciones

Nuevo endpoint: `PUT /api/pricing/import-rates/{importRateId}/review`.
Valida todas las referencias contra Config, actualiza los valores comerciales y publica auditoría antes de guardar.
No requiere migración.
