-- Ejecutar después de desplegar Pricing con la recuperación POD marítimo -> POE
-- y el fallback 40HC para contratos narrativos MSC/ONE NAC. Reintenta únicamente
-- importaciones asíncronas que fallaron porque ninguna fila se consideró utilizable.
-- El payload original se conserva sin modificaciones.
BEGIN;

UPDATE pricing."PricingImportFromExtractionJobs"
SET status = 'RetryScheduled',
    attempt_count = 0,
    next_attempt_at_utc = NOW(),
    lease_owner = NULL,
    lease_expires_at_utc = NULL,
    error_code = NULL,
    error_message = NULL,
    completed_at_utc = NULL,
    updated_at_utc = NOW(),
    version = version + 1
WHERE status = 'Failed'
  AND error_code = 'Pricing.NoUsableExtractionRows';

COMMIT;
