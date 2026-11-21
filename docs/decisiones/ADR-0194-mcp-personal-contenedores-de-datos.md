# ADR-0194 - MCP personal: autoria de contenedores de datos

- Estado: Aceptada
- Fecha: 2026-11-20
- Contexto: ADR-0159 (MCP personal de tableros), ADR-0193 (MCP personal: drones de extraccion),
  modulo Contenedor de datos ("dc", EAV + modelo ER, ADR-0103/ADR-0127/ADR-0132)

## Contexto

El MCP personal de tableros (`/api/mcp/tableros`, ADR-0159) ya permite a una IA externa, con el token `ecx_`
del usuario, operar tareas/tableros, el Directorio, disenar flujos/formularios y armar drones de extraccion
(ADR-0193). Pero NO podia crear ni configurar Contenedores de datos, y eso bloqueo una tarea real: un dron de
extraccion de FOMAG no tenia donde aterrizar los datos porque el contenedor destino no se puede crear desde el
MCP. La regla de trabajo es que el token pueda operar la plataforma por MCP para lo que haga falta.

El modulo expone servicios de aplicacion maduros: `IDataModelService` (contenedor = modelo ER que agrupa
TABLAS + relaciones), `IDataImportConfigService` (conectores + procesos con horario + bitacora) y
`IDataContainerModuleService` + `IMenuConfigService` (publicar una tabla como modulo del menu).

## Decision

Se agrega un bloque de **23 herramientas** al MCP personal (nuevo partial `TableroMcpToolset.Contenedores.cs`),
capa delgada sobre esos servicios, que corre COMO el usuario del token. Abstraccion: "contenedor" = DataModel,
"tabla" = DataContainer, columnas por tabla; los conectores/horarios cuelgan del contenedor (ModelId) y la
publicacion es por tabla raiz. Por eso las herramientas van **contenedor -> tablas -> columnas**.

- Lectura (6): `listar_contenedores_datos`, `ver_contenedor_datos`, `listar_conectores`,
  `listar_programaciones_importacion`, `listar_corridas_importacion`, `ver_publicacion_contenedor`.
- Escritura (10): `crear_contenedor_datos`, `editar_contenedor_datos`, `agregar_tabla`, `editar_tabla`,
  `agregar_columna`, `editar_columna`, `agregar_relacion`, `guardar_conector`,
  `guardar_programacion_importacion`, `publicar_contenedor`.
- Destructivas (7): `eliminar_contenedor_datos`, `eliminar_tabla`, `eliminar_columna`, `eliminar_relacion`,
  `eliminar_conector`, `eliminar_programacion_importacion`, `despublicar_contenedor`.

Detalles:

- **Columnas**: tipos texto/numero/decimal/fecha/booleano/submodelo (los deprecados Reference/RelationMany no se
  ofrecen). Las columnas se persisten via `SaveTableAsync` (replace-all por Id): agregar/editar/eliminar releen
  la tabla, mutan la lista conservando los Id y re-emiten. No se puede borrar una columna con datos (el servicio
  lanza y el MCP devuelve el mensaje limpio).
- **Programaciones**: manual / intervalo (cada N minutos) / cron (5 campos), con modo Append/Replace/Upsert
  (+ columna clave). `listar_corridas_importacion` da la bitacora.
- **Conectores SIN secreto**: `guardar_conector` NUNCA recibe credenciales por el MCP (`Credentials = null`);
  el secreto (clave de API / contrasena de BD / secreto de cliente) se pone en la pantalla y el servicio lo
  cifra (`ISecretProtector`) sin devolverlo jamas. Decidido con el usuario (mas seguro, no se teclean claves en
  el chat del cliente MCP).
- **Publicar (privacidad)**: `publicar_contenedor` resuelve la vista de menu (por nombre o la `IsDefault`) y el
  nodo padre opcional por nombre, y saca la tabla RAIZ como modulo; de ahi la tabla aparece en la matriz de
  Roles y permisos (quien la ve). `despublicar_contenedor` quita el item del menu y conserva la ruta (al
  republicar, los permisos siguen valiendo).
- **Resolucion por nombre o id**: contenedores, tablas, conectores, procesos, vistas y nodos de menu.

### Permiso (decision explicita)

A diferencia de Extraccion (ADR-0193), el modulo Contenedor de datos **SI esta en la matriz de roles**: la
pantalla usa `Perm:contenedor-datos:View`. Por COHERENCIA, las herramientas exigen el permiso efectivo del actor
sobre la clave `contenedor-datos` (`NeedContainer` -> `perms.Can`), con el mismo criterio que flujos: lecturas =
View, `crear_contenedor_datos` = Create, `eliminar_*`/`despublicar` = Delete, el resto = Edit. Owner/Admin y
usuarios sin rol = acceso total. El aislamiento multi-tenant lo garantiza el filtro global de EF.

### Disponibilidad condicional

Las herramientas solo se publican si `IDataModelService` fue inyectado (nuevos parametros OPCIONALES del
constructor de `TableroMcpToolset`: dataModel, dataImport, dataModule, menu). En produccion se inyectan por DI;
en pruebas que no los necesitan, no. Asi las pruebas de catalogo previas siguen validas y se agrego una nueva
con contenedores.

## Consecuencias

- El MCP personal pasa de **101 a 124 herramientas**. Auditoria: toda escritura/destructiva exitosa queda en
  `super_admin_audit_logs` (`mcp.tableros.<tool>`), como el resto del MCP.
- Una IA puede crear un contenedor de punta a punta (tablas, columnas, relaciones), programar su importacion y
  publicarlo, sin la app. Las credenciales de fuentes externas siguen fijandose solo en la pantalla.
- Cobertura dual (PostgreSQL + SQL Server, Testcontainers) en `McpTablerosTests`: catalogo consistente con
  contenedores + flujo funcional crear/estructurar/conector(sin secreto)/programar/publicar/despublicar/borrar.

## Alternativas consideradas

- Exponer "contenedor" como `DataContainer` (tabla simple) en vez de `DataModel`: descartado, porque la pantalla
  actual y la config de importacion (conectores/procesos por ModelId) viven en el modelo ER; habria sido
  incoherente con la UI.
- Aceptar credenciales de conectores por el MCP: descartado por seguridad (se teclearian en el chat del cliente
  MCP); el MCP configura todo menos el secreto.
- Incluir clientes remotos (webhooks) y destino de BD aliada en esta ola: aplazado; el caso comun es
  estructurar + programar + publicar.
