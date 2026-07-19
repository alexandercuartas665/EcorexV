# ADR-0045: Modulo propio para el cliente/agente colmena + bitacora de actividad

- Estado: Aceptado (Ola 1 implementada; Olas 2-5 pendientes)
- Fecha: 2026-07-18
- Contexto de decision: el usuario, al comparar Contenedores de datos y Extraccion de Datos.

## Contexto

La conexion con el **agente colmena** on-prem se identifica con un **cliente** (`ClientId` publico +
secreto cifrado, tabla `data_clients`, tenant-scoped). Hoy:

- El cliente ya se modela UNA sola vez (`data_clients`), pero su ciclo de vida vive **dentro** del
  dominio de Contenedores (`IDataImportConfigService`, namespace `DataContainers`).
- Su **CRUD esta duplicado** en dos paginas: `ContenedorDatos.razor` y `ExtraccionDatos.razor` (el
  "+ Agente" de cada una llama al mismo `SaveClientAsync`). Cada modulo nuevo que use la colmena tendria
  que re-implementar ese alta.
- **No hay modulo propio** del agente ni **bitacora persistida**: la presencia es en memoria
  (`IAgentRegistry`) y la actividad esta dispersa (`ScrapeFlowRun` para extraccion, `ImportRun` para
  contenedores) o es efimera (los hexagonos del panal de la colmena no se guardan).

El cliente/agente colmena es en realidad un **recurso de infraestructura transversal** (una credencial +
una conexion), no algo que pertenezca a Contenedores.

## Decision

Promover el cliente/agente colmena a **modulo propio** (tenant-scoped), ubicado en **Infraestructura IA ->
Agentes Colmena**:

1. **Duenio unico del ciclo de vida**: registrar cliente (genera `ClientId` + secreto mostrado UNA vez),
   editar, **rotar** secreto, **revocar** (deshabilitar sin borrar, para no perder historia) y borrar.
   Presencia/salud reusando `IAgentRegistry` (online, version, host, visto-hace).
2. **Reuso por seleccion**: Contenedores y Extraccion **seleccionan** un cliente existente; se elimina el
   "+ Agente" duplicado.
3. **Bitacora de actividad centralizada** (`AgentActivityLog`, tenant): el **servidor** escribe UN
   registro por orden atendida (resumen: cliente, tipo -navegador/consulta/archivo-, origen -flujo/
   contenedor-, resultado, duracion, error). Asi el log es real y no depende de la colmena.

`data_clients` **no cambia** (0 perdida de datos): solo se refactoriza la propiedad del servicio y se
agrega la tabla de log.

## Plan por olas

1. **Servicio/dominio** (esta ola): extraer el ciclo de vida a `Agents.IAgentClientService`
   (List/Save/RotateSecret/**Revoke**/Delete) reusando `data_clients`, sin migrar datos.
   `DataImportConfigService` **delega** en el nuevo servicio (mapeando DTOs) para no romper a los
   consumidores actuales mientras migran.
2. **Bitacora**: entidad `AgentActivityLog` + EF + migracion dual; el servidor escribe por orden
   (`BrowserActionChannel` y el camino de fetch del gateway). Consulta paginada.
3. **UI del modulo** `/agentes-colmena`: clientes (registrar/rotar/revocar) + presencia + feed de
   actividad con filtros.
4. **Consumidores -> selector**: quitar el "+ Agente"/CRUD de `ContenedorDatos.razor` y
   `ExtraccionDatos.razor`; dejar solo `<select>` de clientes existentes + enlace "gestionar agentes".
5. **Docs + verificacion en vivo**.

## Consecuencias

- (+) Cero duplicacion del alta de clientes; escala a nuevos modulos que usen la colmena.
- (+) Observabilidad central de que hace cada agente (la bitacora que hoy no existe).
- (+) Un lugar para rotar/revocar credenciales y ver salud.
- (-) Refactor + 1 migracion dual (solo la tabla de log) + tocar 2 modulos vivos (Ola 4).
- Nota: la revocacion (Ola 1) marca `IsActive=false`; que el hub RECHACE la conexion de un cliente
  inactivo es un endurecimiento a confirmar en una ola posterior (hoy `IsActive` gobierna la UI/seleccion).
