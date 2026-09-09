# Visibilidad de vendedores

Pricing aplica el alcance comercial en backend, no solamente en la interfaz.

- **Own**: el vendedor ve únicamente sus solicitudes/tarifas.
- **Selected**: ve las propias más los `seller_user_id` asignados en `pricing."SellerVisibilityRules"`.
- **All**: ve todos los vendedores.

La visibilidad propia es implícita y nunca necesita una fila en `SellerVisibilityRules`.

El reporte `/api/pricing/rate-requests/all` y su exportación Excel aplican el mismo filtro. El detalle de una tarifa solicitada también valida el alcance antes de responder.

La delegación es de lectura: el endpoint de decisión comercial conserva la comprobación de propiedad, por lo que un supervisor no puede aceptar o rechazar la tarifa de otro vendedor solamente por poder verla.
