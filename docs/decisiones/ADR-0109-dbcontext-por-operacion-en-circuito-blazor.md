# ADR-0109: DbContext por operacion en el circuito Blazor (scope+Begin, NO IDbContextFactory crudo)

**Estado:** Aceptado
**Fecha:** 2026-09-25
**Deciders:** equipo ECOREX.tareas

## Contexto

`EcorexDbContext` se registra `AddDbContext` = **scoped**. En Blazor Server el scope vive TODO el circuito
(la pestana), no una request. Y los componentes **inicializan/renderizan en PARALELO** y reaccionan a eventos
(clicks, broadcasts SignalR) que se solapan. Resultado: dos flujos async usan el MISMO `DbContext` a la vez y
EF lanza `System.InvalidOperationException: A second operation was started on this context instance...`, que
**tumba el circuito** ("Ha ocurrido un error. Recargar").

Se observo en prod en dos flujos reales: (1) marcar varios items de la lista de chequeo rapido, y (2) abrir el
Directorio (su init chocaba con el init de NavMenu, que corre en todas las paginas).

## Decision

El primitivo para aislar una operacion de BD dentro del circuito es un **scope EF nuevo con el tenant fijado**,
NO un `IDbContextFactory` crudo:

```csharp
using var ambient = AmbientTenantContext.Begin(tenantId, userId);   // el tenant fluye por AsyncLocal
await using var scope = ScopeFactory.CreateAsyncScope();            // DbContext propio, aislado del circuito
var svc = scope.ServiceProvider.GetRequiredService<IMiServicio>();
var data = await svc.LeerAsync(...);
```

Cuando la operacion puede dispararse concurrentemente (un handler que el usuario repite rapido, o un reload por
broadcast), se agrega ademas un `SemaphoreSlim` que **serializa** (ver `TaskKanban.ReloadAsync`, checklist).

## Por que NO `IDbContextFactory` crudo

El aislamiento multi-tenant (regla #1, INVIOLABLE) depende del **query filter global por TenantId**, que se
resuelve de `ITenantContext` (`AmbientTenantContext`: AsyncLocal con fallback a los claims del HttpContext). Un
`AddDbContextFactory` crea contextos con el **service provider raiz** -> `ITenantContext` SIN el tenant del
circuito -> el filtro no aisla -> **fuga cross-tenant**. Un scope nuevo, en cambio, re-resuelve `ITenantContext`
y, con `AmbientTenantContext.Begin`, hereda el tenant correcto. Por eso el patron seguro aqui es scope+Begin.

## Estado de la conversion

- YA aplicado (patron correcto): `NavMenu` (tenant/plan/marca/menu), `MainLayout` (badge/hub notif), `Inicio`,
  `TaskKanban.ReloadAsync` + `OnInitializedAsync`. El shell (siempre presente) quedo aislado, que era el "socio"
  de colision global -> el barrido de los 11 modulos principales quedo sin `second operation`.
- Mitigaciones puntuales: gate en los toggles de la lista de chequeo (`TaskDetailModal`).
- PENDIENTE (deuda, incremental): el resto de paginas que en su `OnInitializedAsync`/handlers usan servicios
  inyectados (DbContext del circuito). Se convierten UNA a la vez al patron scope+Begin, y cada tanda pasa por
  `TenantIsolationTests` (condicion de merge, ADR de multi-tenancy) para garantizar que no se filtro ningun
  tenant. NO se hace big-bang: el riesgo de un error = fuga cross-tenant.

## Consecuencias

- Se elimina la clase de crash "second operation" en los caminos calientes; el resto se endurece incremental.
- Los servicios de ESCRITURA/transaccion siguen en el DbContext del circuito (una operacion multi-tabla en un
  metodo usa un solo contexto; las escrituras del usuario ya se serializan por `_busy`/gates): no se tocan.
- Alternativa descartada: `IDbContextFactory` crudo (rompe la multi-tenencia) y cambiar el DbContext a Transient
  (rompe la unidad de trabajo / transacciones cross-servicio).
