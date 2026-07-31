# Importación asíncrona desde DataExtraction

`PricingImportFromExtractionRequestedStreamHandler` persiste un job idempotente
y retorna. `PricingImportFromExtractionWorker` llama directamente a
`ExtractAndPersistFclPricingImportService.PersistExtractionAsync`; no depende
del endpoint HTTP interno ni vuelve a invocar DataExtraction por gRPC.

Configuración de despliegue:

- `Pricing__ExtractionImportJobs__Enabled=true`
- `Pricing__ExtractionImportJobs__MaxConcurrentJobs=2`
- `Pricing__ExtractionImportJobs__LeaseMinutes=5`
- `Pricing__ExtractionImportJobs__MaxRetryCount=3`

La migración `AddExtractionImportJobs` se aplica al iniciar API/Worker. El
endpoint manual `/api/pricing/rate-import-batches/from-extraction` se conserva
para compatibilidad y pruebas.
