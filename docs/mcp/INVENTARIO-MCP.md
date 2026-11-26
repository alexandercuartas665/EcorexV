# INVENTARIO MCP - ECOREX.tareas

> Inventario y revision de suficiencia de TODOS los servidores MCP y toolsets de IA del sistema.
> Fuente: el codigo (no la memoria) + validacion en vivo contra el servidor local. Revision: 2026-11-12, v0.16.304
> (addendum 2026-11-20: +17 herramientas de Extraccion de datos / drones, ADR-0193, total 101; rama
> `feat/mcp-terminar-tarea`, AUN NO integrada ni validada en vivo)
> (addendum 2026-11-20: +23 herramientas de Contenedor de datos, ADR-0194, total 124; misma rama
> `feat/mcp-terminar-tarea`, AUN NO integrada ni validada en vivo)
> (addendum 2026-11-21: +17 herramientas de Agentes de IA, ADR-0195, total 141; misma rama
> `feat/mcp-terminar-tarea`, AUN NO integrada ni validada en vivo)
> (addendum 2026-11-21: +1 herramienta `clasificar_lote`, ADR-0197, total 142; rama
> `feat/clasificar-lote-contenedor`)
> (addendum 2026-11-21: +9 herramientas de Reportes e indicadores, ADR-0198, total 151; rama `feat/reportes-mcp`)
> (addendum 2026-11-21: +4 herramientas del paso de clasificacion en la importacion + limpiar_marca, ADR-0199, total 155;
> rama `feat/clasificacion-en-importacion`)
> (addendum 2026-11-22: +1 herramienta `rellenar_columna`, ADR-0201, total 156; rama `feat/mcp-rellenar-columna`)
> (addendum 2026-11-23: +1 herramienta `ver_conector` y `guardar_conector` edita solo lo enviado, ADR-0203, total 157;
> rama `feat/mcp-ver-conector`)
> (addendum 2026-11-23: +1 herramienta `guia_ecorex` (manual de los MCP servido desde el cerebro plantilla guia), ADR-0204,
> total 158; rama `feat/guia-mcp-cerebro`)
> (addendum 2026-11-24: +6 herramientas: `editar_reporte`, `archivar_reporte`, `eliminar_indicador`,
> `eliminar_libro_indicadores`, `agregar_filas`, `ver_filas`; y la consulta de reportes/indicadores gana `periodo`
> (dia/semana/mes/trimestre/anio sobre un campo fecha), ADR-0206, total 164; rama `feat/mcp-filas-periodo-reportes`)
> (addendum 2026-11-25: +23 herramientas de CONFIGURACION de flujos por nodo (decision humana, nombre de salida,
> reinicio, tablero/columna, concepto de cierre, plazos, avisos con enlaces de decision, formularios por salida, reglas,
> salto, editar/pausar/reanudar/desenganchar) y `guia_ecorex` gana `tema` (cerebros por tema), ADR-0211, total 187;
> rama `feat/mcp-terminar-tarea`)
> (addendum 2026-11-25: +31 herramientas de REGLAS y OPCIONES de formularios (catalogo de verbos, crear/editar reglas
> con cualquier verbo, al enviar / en campo o boton / por vinculo, condicion entre campos, opciones del registro,
> versiones, enlaces, propiedades de campo), ADR-0211 ola 2, total 218; misma rama)
> (addendum 2026-11-25: +14 herramientas del AGENTE DE IA del paso de un flujo (asignar, instrucciones, correo, web,
> WhatsApp, voz, canales, fallo, seguimiento con ventana, tope, traspaso por salida), ADR-0211 ola 3, total 232)
> (addendum 2026-11-26: +2 de campo sobre ADR-0212: diseno_campo (estilo por control, mezcla) y renombrar_campo
> (field_code en cascada con simulacion); campo_bloqueado pasa a IsReadOnly (no editable para todos), total 234)
> (rama `claude/mcp-autoria-flujos-formularios`, AUN NO integrada a main ni desplegada).
> Mantener este archivo al dia cuando se agregue/quite una herramienta (lo protege la prueba
> `McpCatalog_IsConsistent_AndEveryToolDispatches`).

---

## 1. Resumen

| Servidor | Ruta | Quien lo usa | Auth | Actua como | Herramientas |
|---|---|---|---|---|---|
| MCP PERSONAL de tableros | `POST /api/mcp/tableros` | Usuario final desde Claude/Gemini/Codex | Token personal `ecx_` (Scope `mcp:tableros`, emitido en `/conectar-ia`) | El USUARIO (permisos, bitacora "(via IA)", auditoria) | **232** |
| MCP de ADMINISTRACION | `POST /api/mgmt/agent/mcp` (+ REST `/api/mgmt/agent/tools`) | Operador de plataforma / scripts | Llave `ECOREX_MGMT_API_KEY` (header `X-Ecorex-Mgmt-Key`) + `?tenant=` | "Sistema" (Guid.Empty) | **64** |
| MCP de WHATSAPP por linea (ADR-0164) | `POST /api/mcp/whatsapp/{lineId}` (uno por linea Evolution) | Usuario final; se configura en /lineas -> "Configurar MCP" | Token personal `ecx_` (Scope `mcp:whatsapp`) | El USUARIO (permisos conversaciones/lineas, envios "(via IA)", auditoria `mcp.whatsapp.*`) | **36** |

Actualizacion 2026-11-13 (v0.16.310, rama `claude/mcp-whatsapp-evolution`): tercer servidor, el de WhatsApp por linea
(seccion 2b). Los dos servidores personales comparten host JSON-RPC (`McpJsonRpcHost`) y bloques de configuracion.

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

### 2.1 Nucleo - tareas, tableros y pasos de flujo (16) - `TableroMcpToolset.cs`

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
| terminar_tarea | W | Marca la tarea como Hecho: camina la maquina (pendiente -> en progreso -> hecho) y la deja en la columna de cierre. Idempotente; flujo en curso -> completar_paso |
| comentar | W | Comentario en la bitacora "(via IA)" |
| asignar_tarea | W | Cambia/quita el encargado |
| checklist | W | Agregar/marcar/desmarcar items |
| registrar_tiempo | W | Worklog en minutos |
| tomar_paso | W | Reclama un paso sin asignar |
| completar_paso | W | Cierra el paso (con opcion/ruta en compuertas). Si exige formulario sin enviar, se niega |
| guia_ecorex | R | Manual de uso de los MCP (ADR-0204): sin argumentos = LEEME + titulos; `nota` = nota completa; `buscar` = max 20. Sin gate de modulo |

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

### 2.3 Autoria de FLUJOS (16 + 23 de configuracion) - `TableroMcpToolset.Flujos.cs` / `.FlujosConfig.cs` - permiso `flujos`

| Herramienta | Tipo | Permiso | Que hace |
|---|---|---|---|
| listar_flujos | R | View | Flujos con estado, version, nodos, instancias en marcha |
| ver_flujo | R | View | Grafo (nodos, cargos, formularios, aristas) + AVISOS de revision |
| crear_flujo | W | Create | Borrador nuevo (nace con inicio->fin conectados) |
| importar_bpmn | W | Create | Borrador desde XML BPMN 2.0 |
| nueva_version_flujo | W | Edit | Version borrador de un flujo publicado (ids nuevos, vinculos y cargos copiados) |
| agregar_nodo | W | Edit | Nodo tarea/compuerta/fin; con desde/hasta lo INSERTA y quita la conexion directa |
| renombrar_nodo | W | Edit | Renombra |
| conectar_nodos | W | Edit | Conecta (con nombre y condicion opcionales) |
| eliminar_conexion | W | Edit | Quita una arista |
| condicion_ruta | W | Edit | Condicion de salida de compuerta (`approval == 'X'`) |
| asignar_encargado_nodo | W | Edit | Origen del encargado: politica / hereda_inicio / hereda_anterior / campo_formulario |
| asignar_cargo_nodo | W | Edit | Agrega/quita Cargo o Dependencia (origen politica); responde opciones si no existe |
| poner_formulario_a_nodo | W | Edit | Vincula formulario activo, opcional obligatorio (ADR-0077) |
| publicar_flujo | W | Edit | Publica; si hay AVISOS se niega salvo `forzar=true` (visto bueno del usuario) |
| enganchar_flujo_a_concepto | W | Edit | Concepto -> flujo (las tareas nuevas lo arrancan) |
| eliminar_nodo | D | Delete | Borra nodo (nunca el inicio) |

Configuracion por nodo (ADR-0211, `TableroMcpToolset.FlujosConfig.cs`, una herramienta por propiedad):

| Herramienta | Tipo | Permiso | Que hace |
|---|---|---|---|
| listar_reglas_disponibles | R | View | Catalogo de reglas del tenant para vincular a nodos |
| nombre_salida | W | Edit | Nombre de la arista = opcion de la decision (solo borrador) |
| decision_humana_nodo | W | Edit | Compuerta/fin decidida por su encargado (AllowsAssignment; solo borrador) |
| reinicio_nodo | W | Edit | Abre un ciclo nuevo en otro nodo (solo borrador) |
| tablero_destino_nodo | W | Edit | Tablero + columna al llegar el paso |
| concepto_cierre_nodo | W | Edit | Concepto de cierre (columna de cierre) |
| color_nodo / nota_nodo | W | Edit | Apariencia (PATCH entre ellas) |
| salto_a_flujo_nodo | W | Edit | Arranca otro flujo (tarea hija) |
| plazo_nodo / tiempo_arranque_nodo | W | Edit | SLA y espera de arranque (dias habiles opcional) |
| agregar_notificacion_nodo | W | Edit | Aviso al llegar (correo/WhatsApp/grupo/Telegram) con enlaces publicos de decision |
| quitar_notificacion_nodo | D | Edit | Quita un aviso por numero |
| quitar_formulario_nodo | D | Edit | Desvincula formulario |
| formulario_obligatorio_por_salida | W | Edit | Obligatorio solo en ciertas salidas de compuerta |
| formulario_crear_al_llegar | W | Edit | Crear (o no) el formulario al llegar |
| agregar_regla_nodo / regla_autonoma_nodo | W | Edit | Vincula regla existente / autonoma |
| quitar_regla_nodo | D | Edit | Desvincula regla |
| editar_flujo / pausar_flujo / reanudar_flujo | W | Edit | Propiedades (PATCH) y pausa |
| desenganchar_flujo_de_concepto | D | Edit | Concepto deja de arrancar el flujo |

Avisos de revision (no reemplazan al motor; evitan que una IA publique un flujo roto): rama paralela involuntaria
(el motor sigue TODAS las salidas de un nodo que no es compuerta), nodo sin entrada, nodo sin salida, compuerta con
menos de 2 salidas, paso por politica sin cargo (incluye compuerta/fin con decision humana), flujo sin paso de tarea,
compuerta automatica con varias salidas sin condicion, compuerta automatica con salidas SIN NOMBRE (ADR-0211).

### 2.3a Agente de IA del paso (14) - `TableroMcpToolset.FlujosAgente.cs` (ADR-0211 ola 3) - permiso `flujos`

Lectura: opciones_agente_nodo, ver_agente_nodo. Destructiva: quitar_agente_nodo. Escritura: agente_nodo,
instrucciones_agente_nodo, correo_agente_nodo, web_agente_nodo, whatsapp_agente_nodo, voz_agente_nodo,
canales_agente_nodo, fallo_agente_nodo, seguimiento_agente_nodo, tope_preguntas_agente_nodo,
traspaso_salida_agente_nodo. Recursos como PATCH (cambiar uno conserva los demas).

### 2.3b Reglas y opciones de FORMULARIOS (31) - `TableroMcpToolset.FormulariosConfig.cs` (ADR-0211 ola 2)

Permiso: catalogo de reglas (listar_verbos_regla, listar_reglas, ver_regla, historial_regla, crear_regla,
parametros_regla, estado_regla, renombrar_regla, eliminar_regla) = modulo `reglas`; el resto = `formularios`.
Lectura: listar_verbos_regla, listar_reglas, ver_regla, reglas_de_formulario, historial_regla,
listar_versiones_formulario, listar_enlaces_formulario. Destructivas: eliminar_regla, quitar_regla_al_enviar,
quitar_regla_de_campo, restaurar_version_formulario, revocar_enlace_formulario, renombrar_campo. Escritura: regla_al_enviar,
regla_en_campo, parametros_regla_en_campo, condicion_entre_campos, ocultar_barra_envio, formulario_reportable,
codigo_automatico_formulario, registro_editable, cierre_automatico_formulario, guardar_version_formulario,
campo_bloqueado, campo_oculto, valor_por_defecto_dinamico, permisos_campo_por_rol, subformulario_de_campo, diseno_campo.
Detalle y parametros: `docs/vault-mcp-formularios/Referencia/FO11 - Referencia de herramientas de reglas y opciones.md`.

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

### 2.5 Extraccion de datos / drones (17) - `TableroMcpToolset.Extraccion.cs` (ADR-0193)

Capa delgada sobre `IScrapeFlowService` (CRUD del modulo 000730 /extraccion-datos). Solo CONFIGURACION: el runtime
(sub-agente Navegador) es diferido, igual que en la pantalla. **Permiso**: la policy `ExtraccionDatos.Editar` solo
exige `tenant_id` (NO esta en la matriz de roles); por coherencia estas herramientas quedan para CUALQUIER portador
del token, sin gate de rol extra. Solo se publican si `IScrapeFlowService` esta inyectado (lo esta por DI en prod).

| Herramienta | Tipo | Que hace |
|---|---|---|
| listar_drones | R | Drones del tenant: id, nombre, estado, pasos, contenedor, ultima corrida |
| ver_dron | R | Dron completo: cabecera + pasos ordenados + variables (sin secretos) + programaciones |
| listar_destinos_extraccion | R | Tablas destino ("Modelo / Tabla") para cabecera o paso |
| listar_reglas_extraccion | R | Reglas del tenant para el paso tipo ensamblado |
| listar_corridas | R | Bitacora de ejecuciones (resultado, insertados/actualizados, detalle) |
| listar_programaciones | R | Programaciones del dron (varias por dron, ADR-0171) |
| crear_dron | W | Cabecera nueva (nombre + url_inicio obligatorios; estado, contenedor). Devuelve dron_id |
| editar_dron | W | Cambia solo los campos de cabecera enviados |
| agregar_paso | W | Paso al final (o en 'orden'); 24 tipos por alias; config fina en mapeo_json. Devuelve paso_id |
| editar_paso | W | Cambia solo los campos del paso enviados (puede cambiar el tipo) |
| reordenar_pasos | W | Reordena por lista de paso_id |
| definir_variable | W | Upsert por nombre; secretas cifradas (ISecretProtector), nunca devueltas en claro |
| guardar_programacion | W | Alta/edicion: manual / intervalo / cron + barrido por tramos (FlowSequenceSpec) |
| eliminar_paso | D | Borra un paso (confirmar) |
| eliminar_variable | D | Borra una variable (confirmar) |
| eliminar_programacion | D | Borra una programacion (confirmar) |
| eliminar_dron | D | Borra el dron completo (confirmar) |

Fuera de esta ola (candidatos futuros): export/import de pasos, revelar secreto, desencadenador estilo Windows
(`ScheduleTrigger`), disparar una corrida.

### 2.6 Contenedor de datos (26) - `TableroMcpToolset.Contenedores.cs` + `TableroMcpToolset.Filas.cs` (ADR-0194, ADR-0203, ADR-0206) - permiso `contenedor-datos`

Capa delgada sobre `IDataModelService` (contenedor = modelo ER que agrupa tablas + relaciones),
`IDataImportConfigService` (conectores + procesos con horario + bitacora) y `IDataContainerModuleService` +
`IMenuConfigService` (publicar una tabla al menu). Abstraccion: contenedor -> tablas -> columnas; conectores/
horarios por contenedor; publicar por tabla raiz. **Permiso**: el modulo SI esta en la matriz de roles
(`Perm:contenedor-datos:View`), asi que se exige el permiso del actor (View/Create/Edit/Delete), como en flujos.
Conectores SIN secreto: el MCP nunca recibe credenciales (se ponen en la pantalla). Solo se publican si
`IDataModelService` esta inyectado (lo esta por DI en prod).

| Herramienta | Tipo | Permiso | Que hace |
|---|---|---|---|
| listar_contenedores_datos | R | View | Contenedores: id, nombre, #tablas, #relaciones |
| ver_contenedor_datos | R | View | Tablas (con columnas tipo/requerido/derivada) + relaciones |
| crear_contenedor_datos | W | Create | Cabecera nueva (nombre + descripcion). Devuelve contenedor_id |
| editar_contenedor_datos | W | Edit | Cambia nombre/descripcion |
| eliminar_contenedor_datos | D | Delete | Borra el contenedor completo (cascada) |
| agregar_tabla | W | Edit | Tabla con columnas opcionales. Devuelve tabla_id |
| editar_tabla | W | Edit | Renombra/redescribe (conserva columnas) |
| eliminar_tabla | D | Delete | Borra una tabla (columnas y filas) |
| agregar_columna | W | Edit | Columna nueva (texto/numero/decimal/fecha/booleano/submodelo) |
| editar_columna | W | Edit | Cambia nombre/tipo/requerido de una columna |
| eliminar_columna | D | Delete | Borra una columna (no si tiene datos) |
| agregar_relacion | W | Edit | Relacion n1/nn entre dos tablas del mismo contenedor |
| eliminar_relacion | D | Delete | Borra una relacion por id |
| listar_conectores | R | View | Conectores (fuentes) del contenedor: tipo, destino, si tiene credenciales |
| ver_conector | R | View | Config completa SIN secretos: endpoint (parametros sensibles ***), metodo, auth, nombres de headers, mapeo_json (campos + paginacion); bd: motor/host/puerto/base/usuario/query (ADR-0203) |
| guardar_conector | W | Edit | Alta/edicion (excel/api/bd/tabla) SIN secreto (se pone en pantalla); en edicion solo cambia lo enviado (ADR-0203) |
| eliminar_conector | D | Delete | Borra un conector |
| listar_programaciones_importacion | R | View | Procesos con horario: tipo, cada cuanto, modo, activo, proxima |
| guardar_programacion_importacion | W | Edit | Alta/edicion: manual/intervalo/cron + modo append/replace/upsert |
| eliminar_programacion_importacion | D | Delete | Borra un proceso |
| listar_corridas_importacion | R | View | Bitacora del proceso (trigger, resultado, filas, detalle) |
| ver_publicacion_contenedor | R | View | Si la tabla esta publicada: ruta, icono, columnas de grilla/filtro |
| publicar_contenedor | W | Edit | Saca la tabla raiz al menu (vista por nombre o defecto; padre opcional) |
| despublicar_contenedor | D | Delete | Quita la tabla del menu (conserva la ruta y los permisos) |
| agregar_filas | W | Edit | ADR-0206: hasta 100 filas por llamada; valida tipo por columna y obligatorias (todo o nada); upsert con `columna_clave` |
| ver_filas | R | View | ADR-0206: lectura acotada (filtros columna+texto, tope 100), las mas recientes primero |

Fuera de esta ola (candidatos futuros): clientes remotos (webhooks) + secreto, destino de BD aliada, filas
(upsert de registros ya existe en el toolset de agente), tablas derivadas (ADR-0127), disparar una importacion.

### 2.7 Agentes de IA (17) - `TableroMcpToolset.Agentes.cs` (ADR-0195) - permiso `agentes`

Capa delgada sobre `IAiAgentService` (modulo 000867). Abstraccion: agente -> prompts enrutados + recursos;
proveedor/modelo/prompt/lector de medios/herramientas on-off/tableros/round-robin/concepto. **Permiso**: el
modulo SI esta en la matriz de roles (clave `agentes`), asi que se exige el permiso del actor (View/Create/
Edit/Delete), como en flujos y contenedores. Nace APAGADO (crear y duplicar); se enciende tras validar. Las
ediciones son por campo (relee y fusiona, porque `UpdateAsync` es reemplazo total). El agente escribe a un
contenedor en runtime con `agregar_filas_a_contenedor`, HABILITADA por defecto (DisabledTools es whitelist por
exclusion). Solo se publican si `IAiAgentService` esta inyectado (lo esta por DI en prod).

| Herramienta | Tipo | Permiso | Que hace |
|---|---|---|---|
| listar_agentes | R | View | Agentes: id, nombre, rol, proveedor, modelo, encendido, #recursos |
| ver_agente | R | View | Agente completo: prompt, lector de medios, herramientas, tableros, recursos, prompts |
| historial_prompts_agente | R | View | Versiones del historial de prompts (ultimas 5), por agente o por prompt |
| crear_agente | W | Create | Agente nuevo (proveedor claude/gemini/chatgpt/deepseek). Nace apagado. Devuelve agente_id |
| editar_agente | W | Edit | Nombre/rol/proveedor/modelo/prompt/herramientas/tableros/round-robin/concepto (por campo) |
| agente_lector_medios | W | Edit | Fija/limpia el prompt con que lee cada PDF/imagen |
| encender_agente | W | Edit | Pone el agente en produccion |
| apagar_agente | W | Edit | Saca el agente de produccion |
| duplicar_agente | W | Edit | Copia config/prompts/recursos (sin linea de atencion). La copia queda apagada |
| restaurar_version_prompt | W | Edit | Restaura una version del historial de prompts |
| agregar_recurso_agente | W | Edit | Recurso (imagen/video/audio/pdf/ubicacion/texto). Devuelve recurso_id |
| editar_recurso_agente | W | Edit | Cambia nombre/tipo/detalle/archivo de un recurso (por campo) |
| agregar_prompt_agente | W | Edit | Prompt enrutado (nombre + regla + cuerpo). Devuelve prompt_id |
| editar_prompt_agente | W | Edit | Cambia nombre/regla/cuerpo de un prompt enrutado (por campo) |
| eliminar_agente | D | Delete | Borra el agente completo (recursos, prompts, config) |
| eliminar_recurso_agente | D | Delete | Borra un recurso por id |
| eliminar_prompt_agente | D | Delete | Borra un prompt enrutado por id |

Fuera de esta ola (candidatos futuros): cierre del agente, reactivacion/seguimiento y datos-cache (jsonb que la
pantalla fija aparte; no estan en `IAiAgentService`).

### 2.8 Clasificacion por lotes y paso de importacion (5) - `TableroMcpToolset.Clasificacion.cs` (ADR-0197, ADR-0199) - permiso `contenedor-datos` (Edit)

Capa delgada sobre `IContenedorClasificacionService`. Toma hasta N filas (max 50) de una tabla cuya columna marca
esta vacia, se las envia a un agente de IA ENCENDIDO y escribe SOLO las columnas de salida + la marca (fecha UTC)
por la columna clave (no duplica). Devuelve `restantes` para repetirla lote a lote. Consume tokens (AiUsageLog,
fuente `clasificar-lote`) y respeta el cupo duro del plan. Se publica solo si el runner y el modulo de
contenedores estan inyectados. Complemento del agente: `actualizar_filas_por_clave` en `ContenedorDatosToolset`.

| Herramienta | Tipo | Permiso | Que hace |
|---|---|---|---|
| clasificar_lote | W | Edit | Clasifica un lote de filas pendientes con un agente y escribe salida + marca por clave |
| limpiar_marca | W (destructiva) | Edit | ADR-0199: vacia la marca (y opcionalmente las salidas) de todas las filas o por filtro {columna: valor}, para reclasificar |
| rellenar_columna | W | Edit | ADR-0201: llena una columna de las filas existentes con una plantilla de otras columnas de la misma fila ("{a}|{b}"); por defecto solo vacias, 20000 filas por llamada en una transaccion |
| configurar_paso_clasificacion | W | Edit | ADR-0199: paso de clasificacion en una programacion de importacion; tras cada corrida ok clasifica solo lo nuevo (tope de lotes, cupo del plan) |
| quitar_paso_clasificacion | W | Edit | ADR-0199: quita el paso (la importacion sigue igual) |
| ejecutar_clasificacion_ahora | W | Edit | ADR-0199: encola la clasificacion de la programacion; el worker la corre en < 1 min y deja el resumen en la bitacora |

### 2.9 Reportes e indicadores (13) - `TableroMcpToolset.Reportes.cs` (ADR-0198) - permisos `reportes/*` y `analisis`

Capa delgada sobre el motor de reportes (ADR-0051) y los libros de indicadores (ADR-0158). Cada TABLA raiz de un
contenedor es su propia fuente `container:{id_tabla}`; la fuente se indica por clave, por `contenedor` + `tabla` o
por nombre. La consulta va en NOMBRES de campo (se traducen a las claves del catalogo) y se EJECUTA antes de guardar:
si no corre, no se guarda nada. v1 solo paneles (los imprimibles RDL se disenian en pantalla). ADR-0206: la consulta
admite `periodo` (dia, semana, mes, trimestre, anio) para agrupar un campo FECHA en orden cronologico ("ventas por mes").

| Herramienta | Tipo | Permiso | Que hace |
|---|---|---|---|
| listar_fuentes_reporte | R | reportes/galeria o analisis View | Fuentes (tablas de contenedor con su contenedor, nativas, formularios) |
| ver_fuente_reporte | R | reportes/galeria o analisis View | Campos de una fuente: tipo y si filtra/agrupa/suma |
| listar_reportes | R | reportes/galeria View | Reportes visibles (reportes/admin ve todos) |
| ver_reporte | R | reportes/galeria View | Ejecuta un reporte guardado: columnas, total y muestra |
| crear_reporte | W | reportes/ia Create o reportes/admin View | Panel tabla/barras/torta/linea con categoria opcional |
| listar_libros_indicadores | R | analisis View | Libros visibles |
| ver_libro_indicadores | R | analisis View | Hojas y widgets de un libro |
| crear_libro_indicadores | W | analisis Create/Edit | Libro privado con su Hoja 1 |
| agregar_indicador | W | analisis Create/Edit (dueno o admin) | Widget kpi/barras/dona/embudo/treemap/linea/area/tabla; crea la hoja si no existe |
| editar_reporte | W | reportes/ia Create o reportes/admin View | ADR-0206: cambio parcial (nombre, categoria, descripcion, grafico, consulta); valida ejecutando |
| archivar_reporte | D | reportes/ia Create o reportes/admin View | ADR-0206: archiva (recuperable desde Archivados en la galeria) |
| eliminar_indicador | D | analisis Create/Edit (dueno o admin) | ADR-0206: quita un widget de su libro |
| eliminar_libro_indicadores | D | analisis Create/Edit (dueno o admin) | ADR-0206: borra el libro con sus hojas (soft-delete) |

---

## 2b. MCP de WHATSAPP por linea Evolution - `/api/mcp/whatsapp/{lineId}` (ADR-0164)

- Un servidor por linea (en el cliente: `whatsapp-ventas`, `whatsapp-soporte`...). URL, token y bloques por cliente en
  /lineas -> "Configurar MCP". La linea se resuelve con el filtro global (otra empresa -> 404). Nunca expone llaves.
- Envios 1:1 por la capa de chat: quedan en Conversaciones a nombre "Usuario (via IA)" y SARA cede el turno.
- Salvaguardas: 1 destinatario por llamada; primer contacto solo con `primer_contacto_confirmado=true`; 20 envios/min y
  300/hora por linea; estados exigen `para_todos` o lista. Media por https publica (anti-SSRF, 16 MB) o base64 (2 MB).
- Permisos: modulo `conversaciones` (Ver/Crear/Editar/Eliminar) y `lineas` (perfil, privacidad, reinicio).

| Grupo | Herramientas | Permiso |
|---|---|---|
| Lectura (R) | estado_linea, listar_chats, leer_mensajes, buscar_contactos, verificar_numeros, ver_perfil_contacto, descargar_media (la IA ve/oye el archivo), listar_grupos, ver_grupo, listar_etiquetas, catalogo_business | conversaciones Ver |
| Envio (W) | enviar_texto (con cita), enviar_media (imagen, video, documento, nota de voz, sticker, video_nota), enviar_ubicacion, enviar_contacto, enviar_encuesta, enviar_lista, publicar_estado, reaccionar, escribiendo | conversaciones Crear |
| Gestion (W) | editar_mensaje, marcar_leido, gestionar_chat (archivar, no leido, bloquear), etiquetar_chat, pausar_agente / reanudar_agente (lista negra), guardar_contacto (Directorio) | conversaciones Editar (guardar_contacto: crear persona en Directorio) |
| Grupos (W) | crear_grupo, editar_grupo, participantes_grupo, invitacion_grupo | conversaciones Editar |
| Destructivas (D) | borrar_mensaje, salir_grupo | conversaciones Eliminar |
| Linea (W) | perfil_linea, privacidad_linea (ver = Ver), reiniciar_linea | lineas Editar |

Excluido a proposito: logout, borrar/crear instancia, webhook, proxy, settings, llamadas, plantillas Cloud API.

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
| Reportes / analisis (indicadores) | CUBIERTO (ADR-0198) | 9 herramientas: fuentes y campos, reportes tipo panel (crear/listar/ejecutar) y libros de indicadores (crear libro, agregar widget). Fuera: imprimibles RDL, editar/borrar reportes o widgets, datasets (`datos` sigue solo en el MCP admin) |
| Inventario / Contenedor de datos (consulta) | NO | Existen toolsets de agente: candidatos a puente de lectura |
| Conversaciones WhatsApp (lineas Evolution) | CUBIERTO (MCP de WhatsApp, 2b) | Leer, enviar todo tipo de mensaje, gestionar chats, etiquetas, grupos, pausar SARA, perfil de la linea. Lineas YCloud/Cloud: no (otro proveedor) |
| Notificaciones | NO | Lectura util |
| Pipeline / CRM | NO | Solo `crear_lead` en agentes |
| Extraccion de datos (drones, 000730) | CUBIERTO (ADR-0193) | 17 herramientas: crear/editar/ver/listar/borrar dron, pasos (24 tipos, config fina en mapeo_json), variables (secretos cifrados), programaciones (intervalo/cron/tramos) y bitacora de corridas. Runtime del Navegador diferido (solo config) |
| Contenedor de datos (crear/estructurar/programar/publicar) | CUBIERTO (ADR-0194) | 23 herramientas: contenedor+tablas+columnas+relaciones, conectores (SIN secreto), programaciones (manual/intervalo/cron + modo) + bitacora, y publicar/despublicar como modulo del menu (privacidad por Roles). Carga de filas (upsert) sigue en el toolset de agente |
| Agentes de IA (crear/configurar/encender) | CUBIERTO (ADR-0195) | 17 herramientas: crear agente (claude/gemini/chatgpt/deepseek, nace apagado), editar por campo (prompt/herramientas/tableros/round-robin/concepto), lector de medios, prompts enrutados + recursos, historial, duplicar, encender/apagar, borrar. Escribe al contenedor con `agregar_filas_a_contenedor` (habilitada por defecto). Fuera: cierre/reactivacion/cache |
| Clasificar filas de un contenedor con IA | CUBIERTO (ADR-0197, ADR-0199) | `clasificar_lote`: lotes de filas sin marca -> agente -> columnas de salida + marca por clave; el agente tambien tiene `actualizar_filas_por_clave`. ADR-0199: paso automatico post-importacion (`configurar_paso_clasificacion` / `quitar_` / `ejecutar_clasificacion_ahora`) y `limpiar_marca` para reclasificar |
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
| ADR-0193 (extraccion) / ADR-0194 (contenedor de datos) / ADR-0195 (agentes de IA) | Nuevos; 17 + 23 + 17 herramientas en la rama `feat/mcp-terminar-tarea` |
| ADR-0067 (admin) | Existe |
| `instructions` del MCP personal | Completas: reglas de flujo en curso, contactos, autoria (insertar entre nodos, avisos, forzar, enlaces publicos) |
| Descripciones y esquemas | 164/164 con descripcion >= 25 caracteres y JSON-Schema valido (84 verificadas en vivo; las 17 de extraccion + 23 de contenedor + 17 de agentes por prueba, pendientes de validar en vivo) |
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
