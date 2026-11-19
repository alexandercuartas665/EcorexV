# ADR-0189: Plantilla "Orden de compra" sembrada en todos los tenants

Fecha: 2026-11-19
Estado: Aceptada
Version: v0.16.354
Relacionado: v0.16.344 (imprimir la OC con plantillas del sistema, tokens {oc.*}), plantillas de documento (ola 1)

## Contexto

La v0.16.344 (sesion Compras UX) hizo que las plantillas de documento resuelvan los tokens {oc.*} de una
actividad de orden de compra (numero, proveedor, tabla de lineas, subtotal, descuento, IVA y total). La plantilla
"Orden de compra" y su grupo "Compras" (membrete {empresa.*}) solo se crearon como DATOS en el tenant ANDINAS de
dev. El usuario pidio (2026-11-19) sembrarla en todos los tenants.

## Decision

1. `DatabaseSeeder.EnsureOrdenCompraTemplateAsync`, backfill idempotente que corre en cada arranque (Development y
   produccion, junto a los demas backfills cross-tenant). Cubre tambien los tenants creados despues.
2. Por cada tenant no archivado sin la marca `seed.plantilla_orden_compra` (TenantConfiguration):
   - si no tiene un grupo "Compras" ni una plantilla "Orden de compra", crea el grupo (membrete {empresa.*}) y la
     plantilla (tokens {oc.*}), con el mismo contenido validado en ANDINAS;
   - deja la marca en ambos casos.
3. **Una sola vez por tenant**: la marca evita resucitar la plantilla si el tenant la borra o la renombra (las
   plantillas de documento se borran fisicamente, no hay soft-delete que sirva de lapida).
4. **No se liga a ningun concepto**: la subcategoria de la OC depende de la configuracion de cada tenant (sin id
   fijo). Cada tenant asigna el grupo "Compras" a su concepto de OC, como ya funciona el sistema de plantillas.

## Alternativas descartadas

- **SQL directo en prod**: no cubre tenants nuevos ni queda versionado ni probado en ambos motores.
- **Sembrar solo si no existe (sin marca)**: resucitaria la plantilla en cada arranque si el tenant la borra.
- **Ligarla al concepto de compras**: no hay un concepto fijo comun a todos los tenants.

## Consecuencias

- Todos los tenants ven "Orden de compra" en Plantillas de documento desde el primer arranque con v0.16.354.
- Sin migraciones (filas de datos en tablas existentes).
