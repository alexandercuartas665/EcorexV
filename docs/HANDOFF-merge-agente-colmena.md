# HANDOFF - Unificar `feat/agente-colmena-gui` al tronco

> Prompt listo para pegar en la **sesion principal** (la que trabaja el tronco
> `fase-0/clon-backbone`). Escrito el 2026-07-19. Objetivo: integrar el cuerpo de trabajo
> "Extraccion de Datos + Agente Colmena (ADR-0045)" que ya esta probado en vivo.

---

## Prompt para la sesion principal

Necesito que unifiques la rama **`feat/agente-colmena-gui`** al tronco **`fase-0/clon-backbone`**
(repo `origin` = EcorexV). Son **17 commits** por encima del tronco (merge-base `1491e21`), todos de
un mismo cuerpo coherente: el modulo **Extraccion de Datos** (Olas 1-5) y el modulo **Agentes Colmena**
(ADR-0045, Olas 1-5) + el diag log del agente. La rama esta pusheada a `origin/feat/agente-colmena-gui`.

### Que trae (resumen)

- **Extraccion de Datos** (flujos de automatizacion de navegador config-sin-codigo): dominio
  `ScrapeFlow/Step/Variable`, UI configuradora, compilador flujo->BrowserAction[] con JS FIRMADO por
  el secreto del cliente, runtime determinista + paso de IA, programacion via `import_processes.flow_id`.
- **Agentes Colmena (ADR-0045)**: el cliente/agente on-prem como **modulo propio** (Infraestructura IA).
  `IAgentClientService` duenio del ciclo de vida de `data_clients`; Contenedores y Extraccion pasan a
  SELECCIONAR el agente (se quito el CRUD duplicado). Bitacora transversal `AgentActivityLog`. Pagina
  `/agentes-colmena` (clientes + presencia + feed). Item de menu bajo Infraestructura IA con backfill
  idempotente. Diag log del Servicio a `%PUBLIC%\Documents\ecorex-agent-diag.log`.
- **Concurrencia de la colmena**: una WebView2 efimera y aislada POR ORDEN + despacho de cada orden en
  su propia Task -> varias ordenes simultaneas abren varios navegadores en paralelo. VERIFICADO EN VIVO
  (2 navegador + 2 gateway a la vez, 20 filas ingestadas).

### CRITICO al integrar

1. **Migraciones DUALES nuevas** (regla 2 de CLAUDE.md). Deben quedar en AMBOS proveedores y aplicar
   limpio en PG y SQL Server:
   - `AgentActivityLog` (PG: `20260719005310_AgentActivityLog`; SqlServer: `20260719005424_AgentActivityLog`).
   - `AddConnectorQuery` (PG: `20260717025247`; SqlServer: `20260717025546`).
   - Verifica `dotnet ef migrations has-pending-model-changes` = "No changes" en ambos contextos tras
     el merge (EcorexDbContext y SqlServerEcorexDbContext).
2. **Archivos de alto contacto** (posibles conflictos con trabajo del tronco): `DatabaseSeeder.cs`
   (nuevo `EnsureAgentesColmenaMenuItemAsync` + item de menu 000868), `Program.cs` (SuperAdmin: cableado
   del backfill en el arranque; Agent.Service: registro del FileLoggerProvider), `EcorexDbContext.cs` +
   `IApplicationDbContext.cs` (DbSet `AgentActivityLogs`), `ContenedorDatos.razor` y `ExtraccionDatos.razor`.
3. **Fakes de test**: `RowIngestServiceTests.cs` y `TenantUserServiceTests.cs` implementan
   `DbSet<AgentActivityLog> AgentActivityLogs`. Si el tronco agrega otro DbSet a IApplicationDbContext,
   re-sincronizar los fakes (compilar la SOLUCION completa, no solo SuperAdmin, para atrapar esto).
4. **Nuevo proyecto en la solucion**: el agente vive en `apps/agent` (`Ecorex.Agent.Service/Gui/Core` +
   `libs/Ecorex.Contracts.Agent`), fuera de `Ecorex.sln`. El agente referencia SOLO
   `libs/Ecorex.Contracts.Agent`, NUNCA el backend web.

### Gates de merge (checklist CLAUDE.md, seccion 8 + ADR-0018)

- `dotnet build` Release verde en `apps/backend/Ecorex.sln`.
- `dotnet format` sin cambios.
- `dotnet test` verde (unitarios) + matriz DUAL de integracion (PG + SQL Server via Testcontainers,
  requiere Docker). Incluye el **test de aislamiento cross-tenant** en AMBOS motores (condicion de merge).
- `gitleaks` limpio. NO hay secretos versionados en la rama (el secreto del cliente se muestra una vez y
  se guarda cifrado; el JS del navegador va firmado; la cadena LAN nunca se commitea).
- Actualizar `INVENTARIO GENERAL.md` del vault si cierra modulo.

### Verificacion ya hecha (no hay que rehacerla, es contexto)

Probado en vivo en BD AISLADA `ecorex_agente` (puerto 5262) con la colmena real elevada: 4 tareas
programadas con el mismo `next_run_at` se pisaron -> 2 WebView2 en paralelo + 2 fetch de gateway, 20
filas ingestadas, feed de actividad poblado, agente En linea bajo el tenant. El item de menu se validó
como usuario cliente tenant (owner@sky-system.local) bajo Infraestructura IA con aislamiento por tenant.

### Estrategia de merge sugerida

Merge commit (no squash: preservar las Olas como historia). Si el tronco avanzo, rebase/merge y resolver
en los archivos de alto contacto de arriba. Tras integrar: correr los gates, aplicar las 2 migraciones
duales en un entorno limpio, y confirmar `has-pending-model-changes` = No changes en ambos contextos.

### Pendiente menor (NO bloquea el merge; anotar como deuda)

- El feed de la UI solo muestra ordenes de **Navegador**; las de **Gateway/fetch** salen en el diag del
  agente pero aun no en el feed (logging de la ruta fetch diferido).
- Permiso propio del modulo Agentes Colmena (hoy reusa `ExtraccionDatos.Editar`).
