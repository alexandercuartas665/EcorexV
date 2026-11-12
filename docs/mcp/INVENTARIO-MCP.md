# INVENTARIO MCP - ECOREX.tareas

> Inventario y revision de suficiencia de TODOS los servidores MCP y toolsets de IA del sistema.
> Fuente: el codigo (no la memoria) + validacion en vivo contra el servidor local. Revision: 2026-11-12, v0.16.304
> (rama `claude/mcp-autoria-flujos-formularios`, AUN NO integrada a main ni desplegada).
> Mantener este archivo al dia cuando se agregue/quite una herramienta (lo protege la prueba
> `McpCatalog_IsConsistent_AndEveryToolDispatches`).

---

## 1. Resumen

| Servidor | Ruta | Quien lo usa | Auth | Actua como | Herramientas |
|---|---|---|---|---|---|
| MCP PERSONAL de tableros | `POST /api/mcp/tableros` | Usuario final desde Claude/Gemini/Codex | Token personal `ecx_` (Scope `mcp:tableros`, emitido en `/conectar-ia`) | El USUARIO (permisos, bitacora "(via IA)", auditoria) | **83** |
| MCP de ADMINISTRACION | `POST /api/mgmt/agent/mcp` (+ REST `/api/mgmt/agent/tools`) | Operador de plataforma / scripts | Llave `ECOREX_MGMT_API_KEY` (header `X-Ecorex-Mgmt-Key`) + `?tenant=` | "Sistema" (Guid.Empty) | **64** |

Ademas, los 8 toolsets del MCP de administracion son los MISMOS que usan los agentes internos (SARA/WhatsApp,
`AiInferenceService`), filtrados por agente (`AiAgent.DisabledToolsJson`).

ADRs: ADR-0159 (MCP personal + addenda), ADR-0067 (MCP de administracion / autoria de formularios).

---

## 2. MCP PERSONAL - `/api/mcp/tableros` (ADR-0159)

- Transporte: Streamable HTTP MCP 2025-06-18 (acepta 2025-03-26 y 2024-11-05), JSON-RPC 2.0, un mensaje por POST,
  sin sesion. `initialize` devuelve `instructions` (guia de uso para la IA, ~1600 caracteres).
- Auth: `Authorization: Bearer ecx_...`. Revocado / otro alcance / usuario inactivo -> 401 + `WWW-Authenticate`.
- Aislamiento: la unica lectura cross-tenant es resolver el hash del token; luego `AmbientTenantContext.Begin` y el
  filtro global aislan todo.
- Gobierno: cada grupo respeta la matriz de roles del usuario (misma que la pantalla). Owner/Admin y usuario SIN rol
  = sin restriccion (fail-open, regla B2). Visibilidad de tableros = ADR-0117/0136/0038.
- Auditoria: toda herramienta NO de solo lectura y exitosa -> `super_admin_audit_logs` (`mcp.tableros.<tool>`,
  actor = el usuario, reason `mcp-tableros`), con antes/despues donde aplica.
- Anotaciones MCP en cada herramienta: `readOnlyHint` (25), `destructiveHint` (5), `openWorldHint=false`.

Leyenda: R = solo lectura, W = escribe, D = destructiva (la IA debe confirmar). Permiso = modulo/accion exigidos.

### 2.1 Nucleo - tareas, tableros y pasos de flujo (15) - `TableroMcpToolset.cs`

| Herramienta | Tipo | Que hace |
|---|---|---|
| listar_tableros | R | Tableros visibles: codigo, columnas, numero de tareas |
| ver_tablero | R | Tablero por columnas; alcance equipo/mias/sin_asignar/terminadas (fuerza "mias" si el tablero lo limita) |
| buscar_tareas | R | Busca por texto/estado en lo que el usuario puede ver |
| ver_tarea | R | Detalle: descripcion, solicitante, checklist, bitacora, adjuntos y PASO ACTUAL del flujo |
| mis_pasos_pendientes | R | Pasos de flujo asignados o que puede tomar |
| listar_conceptos | R | Conceptos (tipos de actividad), si arrancan flujo o tienen formulario |
| listar_usuarios | R | Usuarios activos (para asignar) |
| crear_tarea | W | Crea tarea; con concepto arranca su flujo |
| mover_tarea | W | Mueve de columna (NO si la gobierna un flujo en curso) |
| comentar | W | Comentario en la bitacora "(via IA)" |
| asignar_tarea | W | Cambia/quita el encargado |
| checklist | W | Agregar/marcar/desmarcar items |
| registrar_tiempo | W | Worklog en minutos |
| tomar_paso | W | Reclama un paso sin asignar |
| completar_paso | W | Cierra el paso (con opcion/ruta en compuertas). Si exige formulario sin enviar, se niega |

### 2.2 Directorio de contactos (12) - `TableroMcpToolset.Directorio.cs` - permiso `directorio-general`

| Herramienta | Tipo | Permiso | Que hace |
|---|---|---|---|
| buscar_contacto | R | View | Nombre/NIT/email/digitos del telefono + filtros tipo/ciudad/sector/estado |
| ver_contacto | R | View | Ficha completa: campos, vinculados, notas, resumen de tareas |
| tareas_del_contacto | R | View | Tareas ligadas al contacto (visibles para el usuario) |
| listar_campos_contacto | R | View | Campos configurables (clave, `clave_completa` ficha.clave, tipo, opciones) |
| crear_contacto | W | Create (sub-permisos crear-empresa/cliente/sospechoso) | Alta sin duplicar NIT/telefono |
| editar_contacto | W | Edit | Cambia solo lo enviado; rechaza NIT/telefono de otro contacto |
| nota_contacto | W/R | Edit / View | Agregar o listar notas |
| vincular_persona_empresa | W | Edit | Liga/desliga persona existente con cargo |
| contacto_interno | W | Edit + crear persona | Persona dentro de la empresa sin duplicar |
| reactivar_contacto | W | Edit | Vuelve a Activo (exige motivo) |
| inactivar_contacto | D | Delete | Baja logica reversible (exige motivo), nunca DELETE |
| vincular_contacto | W | regla de la tarea | Liga contacto a una tarea como cliente |

### 2.3 Autoria de FLUJOS (16) - `TableroMcpToolset.Flujos.cs` - permiso `flujos`

| Herramienta | Tipo | Permiso | Que hace |
|---|---|---|---|
| listar_flujos | R | View | Flujos con estado, version, nodos, instancias en marcha |
| ver_flujo | R | View | Grafo (nodos, cargos, formularios, aristas) + AVISOS de revision |
| crear_flujo | W | Create | Borrador nuevo (nace con inicio->fin conectados) |
| importar_bpmn | W | Create | Borrador desde XML BPMN 2.0 |
| nueva_version_flujo | W | Edit | Version borrador de un flujo publicado (ids nuevos, vinculos y cargos copiados) |
| agregar_nodo | W | Edit | Nodo tarea/compuerta/fin; con desde/hasta lo INSERTA y quita la conexion directa |
| renombrar_nodo | W | Edit | Renombra |
| conectar_nodos | W | Edit | Conecta (con condicion opcional) |
| eliminar_conexion | W | Edit | Quita una arista |
| condicion_ruta | W | Edit | Condicion de salida de compuerta (`approval == 'X'`) |
| asignar_encargado_nodo | W | Edit | Origen del encargado: politica / hereda_inicio / hereda_anterior / campo_formulario |
| asignar_cargo_nodo | W | Edit | Agrega/quita Cargo o Dependencia (origen politica); responde opciones si no existe |
| poner_formulario_a_nodo | W | Edit | Vincula formulario activo, opcional obligatorio (ADR-0077) |
| publicar_flujo | W | Edit | Publica; si hay AVISOS se niega salvo `forzar=true` (visto bueno del usuario) |
| enganchar_flujo_a_concepto | W | Edit | Concepto -> flujo (las tareas nuevas lo arrancan) |
| eliminar_nodo | D | Delete | Borra nodo (nunca el inicio) |

Avisos de revision (no reemplazan al motor; evitan que una IA publique un flujo roto): rama paralela involuntaria
(el motor sigue TODAS las salidas de un nodo que no es compuerta), nodo sin entrada, nodo sin salida, compuerta con
menos de 2 salidas, paso por politica sin cargo, flujo sin paso de tarea.

### 2.4 Autoria de FORMULARIOS (40 de 46) - puente a `IFormAuthoringToolset` (ADR-0067) - permiso `formularios`

Puente: `TableroMcpToolset.Formularios.cs`. Mismas herramientas (nombres nativos en ingles) pero corriendo COMO el
usuario. Lectura -> View; cualquier cambio -> Edit (igual que `Formularios.Disenar`). `ok:false` -> error MCP.

- Descubrimiento (R): describe_components, list_forms, get_form, verify_form, export_form, get_render_urls,
  list_templates, list_data_containers, describe_data_container, list_tercero_fields, list_org_units,
  list_activity_types.
- Estructura (W): apply_form_spec (preferida: formulario completo declarativo e idempotente), create_form,
  import_form, update_form_header, add_container, update_container, move_container, add_question, update_question,
  move_question. Destructivas (D): delete_container, delete_question.
- Comportamiento/ciclo de vida (W): set_transactional, set_sequence_next, set_custom_css, set_theme,
  set_status_ladder, wire_submit_task_rule, activate, deactivate. Destructiva (D): archive.
- Impresion/enlaces/registros (W): create_template, update_template, set_default_template, wire_print_button,
  wire_convert_button, create_share_link (enlace PUBLICO: confirmar con el usuario), create_record.

EXCLUIDAS a proposito (solo en el MCP de administracion), por LISTA PERMITIDA (una herramienta nueva del toolset
no se expone sola):

| Excluida | Motivo |
|---|---|
| list_tenants | Lee TODOS los tenants (`IgnoreQueryFilters`): fuga cross-tenant (regla #1 de CLAUDE.md) |
| list_menu_views, list_menu_nodes, set_module | Configuracion del MENU del tenant: otro modulo y otro permiso |
| create_data_container, add_container_rows | Escritura en el Contenedor de datos: otro modulo y otro permiso (sus lecturas si van) |

---

## 3. MCP de ADMINISTRACION - `/api/mgmt/agent/mcp` (ADR-0067)

- Expone TODOS los `IAgentToolset` registrados. Gate: env `ECOREX_MGMT_API_KEY` (vacia -> endpoint 404), header
  `X-Ecorex-Mgmt-Key` (comparacion en tiempo constante), allow-list IP opcional `ECOREX_MGMT_API_ALLOW_IPS`,
  `?tenant={guid}` obligatorio. Audita las mutaciones como "Sistema".
- Pensado para el operador de plataforma (cross-tenant por diseno): NO darselo a usuarios finales.

| Toolset (GroupKey) | Herramientas | Origen / uso |
|---|---|---|
| form-authoring | 46 (las 40 de 2.4 + list_tenants, list_menu_views, list_menu_nodes, set_module, create_data_container, add_container_rows) | Autoria de formularios |
| tareas | listar_tableros, crear_tarea, consultar_tarea, reenviar_documento | Agente conversacional |
| contenedor-datos | listar_contenedores, agregar_filas_a_contenedor, cargar_productos, consultar_productos, consultar_contenedor | Agente / carga de catalogos |
| datos | listar_datasets, ver_dataset, ejecutar_dataset | Datos externos (conexiones por tenant) |
| inventario | consultar_inventario, ver_item | Agente |
| actividades | ver_formulario_concepto, crear_actividad | Agente (alta por concepto) |
| pipeline | crear_lead | Agente (cierre comercial) |
| directorio | crear_contacto | Agente |

Observaciones (no bloqueantes): `initialize` anuncia protocolo 2024-11-05 y no trae `instructions`; `tools/list`
no trae anotaciones (readOnly/destructive); para la auditoria solo distingue lecturas del toolset de formularios
(las lecturas de los demas quedan auditadas como si mutaran). Los toolsets de agente fueron hechos para una
conversacion de WhatsApp; ejecutados como "Sistema" sin conversacion no se han validado uno por uno.

---

## 4. Validacion realizada (2026-11-12)

1. Pruebas de integracion en matriz DUAL (PostgreSQL + SQL Server, Testcontainers): `McpTablerosTests` 20/20.
   Incluye: token personal, visibilidad, flujo por tomar/completar, contactos y permisos del Directorio, flujo
   completo por IA (crear -> insertar paso -> politica+cargo -> formulario obligatorio -> aviso de rama paralela ->
   eliminar_conexion -> publicar -> enganchar -> nueva_version_flujo -> el concepto pasa a la v2), permisos de
   flujos/formularios, puente seguro (list_tenants/menu ocultos aun para Owner) e integridad del catalogo
   (nombres unicos, descripciones, JSON-Schema valido con `required` coherente, R/D dentro de lo expuesto, toda
   herramienta se despacha, `ok:false` => error).
2. En vivo contra el servidor local (rama, BD local 5442, tenant BITCODE, DI real): `initialize` v0.16.304 protocolo
   2025-06-18; `tools/list` = 83 herramientas, 0 problemas de catalogo con el toolset de formularios REAL; las 6
   excluidas ausentes y `list_tenants` -> "Herramienta desconocida"; lecturas reales OK (listar_flujos, list_forms,
   listar_tableros, listar_conceptos, list_org_units, describe_components). Token de prueba revocado -> 401.
3. Avisos de revision sobre los flujos reales del tenant: los bien construidos dan 0 avisos (sin falsos positivos).
   HALLAZGO en datos: FLW-003 "COMPRAS BITCODE" (en marcha, v3) tiene un nodo suelto "Hola" al que el inicio sale
   en paralelo, sin salida y sin cargo -> cada instancia deja un paso huerfano. (Copia local de prod: verificar en
   prod.)

---

## 5. Suficiencia: lo que la IA puede y no puede hacer con el MCP PERSONAL

Estado: CUBIERTO / PARCIAL / NO / EXCLUIDO (a proposito: admin o seguridad).

| Area (modulo) | Estado | Notas |
|---|---|---|
| Tareas y tableros (operar) | PARCIAL | Falta EDITAR la tarea (titulo, descripcion, prioridad, vencimiento), adjuntar archivos, crear subtareas |
| Pasos de flujo (operar) | PARCIAL | Tomar/completar si. Falta DILIGENCIAR el formulario de un paso (bloquea flujos con formulario obligatorio) y devolver/rechazar/reasignar/reabrir |
| Directorio | CUBIERTO | Falta fusionar duplicados e importar masivo (fuera de alcance declarado) |
| Flujos (disenar) | CUBIERTO (lo esencial) | Falta tablero/columna destino del nodo, plazos (SLA), avisos del nodo, agente IA por nodo, reglas por nodo, reinicio |
| Formularios (disenar) | CUBIERTO | 40 herramientas |
| Conceptos (000270) | PARCIAL | Solo listar. Falta crear/editar concepto (para armar un proceso de punta a punta sin la app) |
| Tableros (configurar) | NO | Crear tablero y columnas |
| Proyectos | NO | Modulo con permiso propio (`proyectos`) sin herramientas |
| Reportes / analisis / datasets | NO | Existe `datos` (listar/ver/ejecutar dataset) solo en el MCP admin: candidato a puente |
| Inventario / Contenedor de datos (consulta) | NO | Existen toolsets de agente: candidatos a puente de lectura |
| Conversaciones WhatsApp / notificaciones | NO | Lectura util; ENVIAR mensajes es saliente y sensible |
| Pipeline / CRM | NO | Solo `crear_lead` en agentes |
| Compras / DIAN / conciliacion / gestor documental | NO | Especializados |
| Usuarios, roles, menu, tenants, servidores, IA, facturacion | EXCLUIDO | Administracion y seguridad: no deben ir por token personal |

Brechas priorizadas (propuesta):

1. ALTA - `editar_tarea` (titulo/descripcion/prioridad/vencimiento/estado) y `diligenciar_formulario_paso`
   (ver formularios del paso + enviar via `IFormResponseService.SaveAsync(submit:true)`): son lo que mas frena el uso
   diario.
2. ALTA - Conceptos y tableros de autoria: `crear_concepto`/`editar_concepto`, `crear_tablero`/`agregar_columna`,
   `tablero_destino_nodo` -> la IA arma un proceso completo (concepto + tablero + flujo + formulario) sin la app.
3. MEDIA - Operaciones de paso: devolver/rechazar/reasignar/reabrir/nota de paso (servicios ya existen).
4. MEDIA - Puentes de LECTURA con permiso: datasets/reportes, inventario, contenedor de datos.
5. MEDIA - Proyectos (listar/ver/crear).
6. BAJA - Conversaciones (solo lectura), notificaciones, adjuntos, subtareas, SLA/avisos/agente por nodo.

---

## 6. Documentacion: estado

| Pieza | Estado |
|---|---|
| ADR-0159 (personal) | Completo, con addenda por fase; conteo corregido en esta revision |
| ADR-0067 (admin) | Existe |
| `instructions` del MCP personal | Completas: reglas de flujo en curso, contactos, autoria (insertar entre nodos, avisos, forzar, enlaces publicos) |
| Descripciones y esquemas | 83/83 con descripcion >= 25 caracteres y JSON-Schema valido (verificado en vivo y por prueba) |
| `/conectar-ia` | Snippets para Claude Code, Gemini CLI, Gemini app, Codex y Claude Desktop |
| Este inventario | Nuevo (docs/mcp/INVENTARIO-MCP.md) |
| Vault (INVENTARIO GENERAL / ADRs) | PENDIENTE de reflejar (repo aparte) |

Deudas de documentacion: nombres mezclados en el MCP personal (espanol nativo + ingles del puente de formularios:
decision consciente para no duplicar el contrato de ADR-0067); el MCP admin no trae `instructions` ni anotaciones;
falta una guia de uso para el usuario final (ejemplos de peticiones) en la app.

---

## 7. Riesgos y pendientes

- OAuth 2.1 pendiente: ChatGPT web y claude.ai web aun no pueden conectarse (solo clientes con header Bearer).
- Sin rate limiting por token ni tokens de solo lectura; un Owner/Admin no ve ni revoca tokens ajenos.
- Usuario SIN rol = sin restriccion (regla B2): con token personal puede disenar flujos/formularios. Es coherente con
  la pantalla, pero conviene asignar roles antes de repartir tokens.
- Tokens personales expuestos en chats: revocar en `/conectar-ia` los que se hayan compartido.

---

## 8. Recorrido de prueba (para cuando este desplegado)

Decirselo en lenguaje natural a la IA conectada con el token. Verificar cada paso en la app.

1. Prechequeo: `/mcp` en Claude muestra `crear_flujo`, `agregar_nodo`, `create_form`... y NO `list_tenants`.
2. Formulario: "Creame el formulario 'Solicitud de compra (prueba IA)' con una seccion 'Datos' y los campos
   Articulo (texto), Cantidad (numero) y Justificacion (texto largo). Activalo." -> menu Formularios, Activo.
3. Flujo: "Crea el flujo 'Aprobacion de compras (prueba IA)'" -> "Inserta un paso de tarea 'Revisar solicitud' entre
   el inicio y el fin" (agregar_nodo con desde/hasta: queda inicio -> Revisar -> fin, SIN conexion directa) ->
   "Que el encargado se herede de quien inicia" -> (opcional) "Ponle el formulario anterior, no obligatorio" ->
   "Muestrame el flujo" (ver_flujo: 2 aristas, sin avisos) -> "Publicalo".
4. Prueba de avisos: "Conecta tambien el inicio con el fin" -> ver_flujo avisa rama PARALELA -> publicar se niega ->
   "Quita esa conexion" (eliminar_conexion).
5. Arranque real: "Engancha el flujo al concepto '<concepto de prueba>'" -> "Crea una tarea con ese concepto en el
   tablero '<tablero>'" (flujo_iniciado) -> "Que pasos tengo pendientes?" (aparece Revisar solicitud) ->
   "Completa el paso" (llega al fin). Si el formulario quedo obligatorio, completar se niega: correcto.
6. Cambio sobre publicado: "Crea una nueva version del flujo y renombra el paso a 'Revisar y aprobar'; publicala" ->
   el concepto queda apuntando a la version nueva.
7. Permisos: con un usuario de rol "solo ver" en flujos/formularios, su IA lista pero no crea.
8. Limpieza: desenganchar el concepto y archivar flujo/formulario/tarea de prueba en la app.
