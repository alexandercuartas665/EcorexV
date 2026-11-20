# ADR-0193 - MCP personal: autoria de drones de extraccion de datos

- Estado: Aceptada
- Fecha: 2026-11-20
- Contexto: ADR-0159 (MCP personal de tableros), modulo 000730 "Extraccion de Datos" (ADR-0025, ADR-0171)

## Contexto

El MCP personal de tableros (`/api/mcp/tableros`, ADR-0159) permite a una IA externa, conectada con el
token personal `ecx_` del usuario, operar tareas/tableros, el Directorio, y disenar flujos y formularios.
El modulo de Extraccion de Datos (los "drones": flujos de navegador que extraen datos y los vuelcan a una
tabla destino) solo se podia configurar en la pantalla `/extraccion-datos`. El usuario pidio poder CREAR y
armar drones por el MCP con su token.

El servicio de aplicacion `IScrapeFlowService` ya expone el CRUD completo de configuracion (cabecera + pasos
+ variables + programaciones + bitacora de corridas); el runtime (el sub-agente Navegador) es DIFERIDO, igual
que en la pantalla: el MCP solo escribe CONFIGURACION.

## Decision

Se agrega un bloque de 17 herramientas al MCP personal (nuevo partial `TableroMcpToolset.Extraccion.cs`),
capa delgada sobre `IScrapeFlowService`, que corre COMO el usuario del token:

- Lectura (6): `listar_drones`, `ver_dron`, `listar_destinos_extraccion`, `listar_reglas_extraccion`,
  `listar_corridas`, `listar_programaciones`.
- Escritura (7): `crear_dron`, `editar_dron`, `agregar_paso`, `editar_paso`, `reordenar_pasos`,
  `definir_variable`, `guardar_programacion`.
- Destructivas (4): `eliminar_paso`, `eliminar_variable`, `eliminar_programacion`, `eliminar_dron`.

Detalles:

- **Pasos**: se exponen los 24 `ScrapeStepKind` por alias en espanol (navegar, extraer, ia, procesar_archivos,
  ...); la configuracion fina de cada tipo viaja en `mapeo_json` (igual que el campo MappingJson de la pantalla),
  para no inflar el esquema con los campos de cada variante.
- **Variables**: `definir_variable` es un upsert por nombre; las secretas se cifran con `ISecretProtector` y
  NUNCA se devuelven en claro (coherente con la pantalla).
- **Programaciones**: `guardar_programacion` soporta manual / intervalo / cron y el barrido por tramos
  (`FlowSequenceSpec`, ADR-0171). El desencadenador estilo Windows (`ScheduleTrigger`) queda fuera de esta ola.
- **Resolucion por nombre o id**: drones, contenedores destino y reglas se pueden referir por id o por nombre.

### Permiso (decision explicita)

La pantalla gobierna el modulo con la policy `ExtraccionDatos.Editar`, que en ECOREX **solo exige el claim
`tenant_id`** (ser usuario del tenant): el modulo NO esta en la matriz de roles por modulo (a diferencia de
"flujos" o "directorio-general"). Por COHERENCIA con la pantalla, las herramientas de extraccion del MCP quedan
disponibles para cualquier portador de un token valido, SIN un gate de rol adicional. El aislamiento multi-tenant
lo garantiza el filtro global de EF (regla #1 de CLAUDE.md), como en todo el MCP. Si en el futuro el modulo pasa
a la matriz de roles, se agregara el gate equivalente (como `NeedFlow`).

### Disponibilidad condicional

Las herramientas solo se publican si `IScrapeFlowService` fue inyectado (ultimo parametro OPCIONAL del
constructor de `TableroMcpToolset`). En produccion se inyecta por DI; en pruebas que no lo necesitan, no. Asi la
prueba de catalogo existente (sin extraccion) sigue valida y se agrego una nueva con extraccion.

## Consecuencias

- El MCP personal pasa de 84 a 101 herramientas. Auditoria: toda escritura/destructiva exitosa queda en
  `super_admin_audit_logs` (`mcp.tableros.<tool>`), como el resto del MCP.
- Una IA puede armar un dron de punta a punta (cabecera -> pasos -> variables -> programacion) sin la app, pero
  NO ejecutarlo: el runtime del Navegador sigue diferido.
- Cobertura dual (PostgreSQL + SQL Server, Testcontainers): catalogo consistente con extraccion + flujo funcional
  crear/paso/variable/programacion/corridas/borrar en `McpTablerosTests`.

## Alternativas consideradas

- Gate por una clave de modulo "/extraccion-datos" en la matriz de roles: descartado, porque el modulo no esta
  en la matriz y gatearlo negaria a un usuario CON rol restrictivo algo que la pantalla SI le permite
  (incoherente). Se prefiere espejar la policy real.
- Exponer un campo por cada variante de paso en el esquema: descartado por tamano; se usa `mapeo_json`.
- Incluir el desencadenador estilo Windows (`ScheduleTrigger`): aplazado; cron/intervalo/tramos cubren el caso
  comun.
