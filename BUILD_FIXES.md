# Correcciones de compilación

- Registrado `IPricingConfigCatalogClient` con `PricingConfigCatalogGrpcClient`.
- Agregado cliente gRPC de Config en `http://localhost:5302`.
- Registro disponible tanto para API como para Workers.
- Corregida la desreferencia nullable de `PortRole` en `CostRepository`.
- Las librerías compartidas no generan `.deps.json`, para evitar colisiones durante builds paralelos.

Ejecutar:

```bash
chmod +x run-dev.sh
./run-dev.sh
```
