# ADR-0211: MCP de tableros - configuracion completa de flujos (ola 1) y cerebros de guia por tema

- Estado: Aceptado (olas 1, 2 y 3 en rama)
- Fecha: 2026-11-25
- Modulo: MCP personal de tableros (ADR-0159) + motor de flujos (ADR-0014/0022/0034) + guia de los MCP (ADR-0204) +
  autoria de formularios por agente (ADR-0067)
- Origen: el usuario pidio "ampliar el poder del MCP para crear formularios y flujos" con un cerebro propio por tema.

## Contexto

El MCP ya CREABA flujos (16 herramientas) y formularios (~40), pero solo armaba el grafo de un flujo: nodos, aristas,
condiciones, encargado, cargo y formulario. Todo lo que la pantalla configura en el panel de cada nodo quedaba fuera,
y existia en `IWorkflowDesignService` como un metodo por propiedad. Ademas, al revisar el motor:

1. **Una compuerta creada por IA se resolvia sola** (no habia como marcarla "decision humana", `AllowsAssignment`).
2. **Las salidas no tenian nombre**: en una compuerta AUTOMATICA las opciones que ve quien cierra el paso anterior son
   los NOMBRES de sus aristas (`WorkflowInboxProjection.ResolveGatewayAhead`) y la condicion `approval == 'X'` los
   enruta. Sin nombres no hay opciones: la receta R03 de la guia producia un flujo que siempre tomaba la ruta por
   defecto.
3. **Dos errores de PERDIDA DE CONFIGURACION en la autoria de formularios** (afectan tambien al chat que crea
   formularios): `update_question`/`apply_form_spec` reconstruian el request sin los campos que el esquema no edita
   (bloqueado/oculto, subformulario, cascada, permisos por rol, default dinamico, caption/numeral/gridcol) y los
   dejaban en blanco; `set_transactional` apagaba "ocultar barra de envio", "reportable" y el codigo automatico, y
   `set_theme` apagaba `keepEditable` ("registro editable").
4. `guia_ecorex` servia UNA sola guia (marca exclusiva `IsMcpGuide`).

## Decision

**Por olas** (decision del usuario). Ola 1 = flujos + cerebros por tema + los dos errores. Ola 2 = reglas y opciones
de formularios + cerebro de formularios. Ola 3 = agente de IA del nodo.

**Una herramienta por propiedad** (decision del usuario), capa delgada sobre los metodos existentes.

### Ola 1 - flujos (+23 herramientas, `TableroMcpToolset.FlujosConfig.cs`, permiso `flujos`: View lectura / Edit resto)

| Herramienta | Servicio | Nota |
|---|---|---|
| nombre_salida | `SetEdgeNameAsync` (NUEVO) | Nombre de arista = opcion de decision; solo borrador; regenera el XML |
| decision_humana_nodo | `SetNodeConfigAsync` | PATCH: conserva el reinicio; solo borrador |
| reinicio_nodo | `SetNodeConfigAsync` | PATCH: conserva la decision humana; solo borrador |
| tablero_destino_nodo | `SetNodeBoardTargetAsync` | tablero/columna por id o nombre; lista columnas si no existe |
| concepto_cierre_nodo | `SetNodeCloseReasonAsync` | el error lista los conceptos del tablero |
| color_nodo / nota_nodo | `SetNodeAppearanceAsync` | PATCH: cambiar uno no borra el otro |
| salto_a_flujo_nodo | `SetNodeJumpAsync` | |
| plazo_nodo / tiempo_arranque_nodo | `SetNodeSlaAsync` / `SetNodeStartDelayAsync` | `StepSla.Build`, dias habiles |
| agregar_notificacion_nodo / quitar_notificacion_nodo | `SetNodeNotifyAsync` | agrega/quita UNA regla de `NodeNotifyConfig`; enlaces de decision validados contra salidas de compuerta |
| quitar_formulario_nodo, formulario_obligatorio_por_salida, formulario_crear_al_llegar | `RemoveNodeFormAsync`, `SetNodeFormRequiredRoutesAsync`, `SetNodeFormAutoCreateAsync` | salidas por id de nodo destino -> BpmnElementId |
| listar_reglas_disponibles, agregar_regla_nodo, quitar_regla_nodo, regla_autonoma_nodo | `ListRuleCatalogAsync`, `AddNodeRuleAsync`, `RemoveNodeRuleAsync`, `SetNodeRuleAutonomousAsync` | solo VINCULA reglas existentes |
| editar_flujo, pausar_flujo, reanudar_flujo | `UpdateDefinitionPropsAsync`, `PauseAsync`, `ResumeAsync` | editar = PATCH |
| desenganchar_flujo_de_concepto | concepto.WorkflowDefinitionId = null | inverso de enganchar |

Ademas: `conectar_nodos` acepta `nombre`; `ver_flujo` devuelve TODA la configuracion por nodo (decision humana,
reinicio, tablero/columna, concepto, salto, plazo, arranque, avisos numerados, formularios con salidas obligatorias y
creacion al llegar, reglas, color, nota) y el nombre de cada arista; la revision del grafo agrega dos avisos:
compuerta automatica con varias salidas sin condicion, y compuerta automatica con salidas SIN NOMBRE (y trata como
paso atendido a la compuerta/fin con decision humana para el aviso de "politica sin cargo"). Destructivas (anotacion):
`quitar_notificacion_nodo`, `quitar_formulario_nodo`, `quitar_regla_nodo`, `desenganchar_flujo_de_concepto`.

Catalogo del MCP de tableros: **164 -> 187**.

### Cerebros por tema

- `BrainTemplate.McpGuideTopic` (varchar 40, null = "general"); migracion `AddBrainTemplateMcpGuideTopic` en PG y SQL
  Server. A lo sumo UNA guia marcada POR TEMA (`SetMcpGuideAsync(id, isGuide, topic, ...)` desmarca la anterior del
  mismo tema en la misma transaccion, auditado). Temas: slug `[a-z0-9_]{1,40}`; la consola sugiere general, flujos,
  formularios.
- `guia_ecorex` gana `tema` (combinable con `nota`/`buscar`); sin argumentos devuelve ademas `temas` (los publicados).
  Mismo origen que ADR-0204: el cerebro sembrado de la empresa o, si no existe, la plantilla publicada.
- Cerebro de flujos: `docs/vault-mcp-flujos/` (12 notas: modelo mental, las dos formas de decidir, 6 recetas,
  trampas, checklist y la referencia F11 GENERADA del codigo por `gen_referencia_flujos.py`). Los scripts del vault
  aceptan `--vault <carpeta>`.

### Errores de formularios

`BuildQuestionRequest` conserva del campo actual lo que el request reemplaza (y acepta `is_locked`/`is_hidden`);
`set_transactional` es PATCH sobre la definicion actual; `set_theme` conserva `keepEditable`.

### Ola 2 - reglas y opciones de formularios (+31 herramientas, `TableroMcpToolset.FormulariosConfig.cs`)

Capa delgada sobre `IRulesEngine` (catalogo de verbos), `IRuleDocumentService`, `IFormDefinitionService`,
`IFormSnapshotService` e `IFormTokenService` (inyectados opcionales; el catalogo solo las expone si estan).

| Grupo | Herramientas | Permiso |
|---|---|---|
| Catalogo de reglas | listar_verbos_regla, listar_reglas, ver_regla, historial_regla, crear_regla, parametros_regla, estado_regla, renombrar_regla, eliminar_regla | modulo `reglas` (View / Create / Edit / Delete) |
| Reglas en un formulario | reglas_de_formulario, regla_al_enviar (`EnsureFormSubmitVerbRuleAsync`, cualquier verbo, idempotente por verbo), quitar_regla_al_enviar, regla_en_campo (campo o boton `Button`), quitar_regla_de_campo, parametros_regla_en_campo (override por vinculo), condicion_entre_campos | modulo `formularios` |
| Opciones | ocultar_barra_envio, formulario_reportable, codigo_automatico_formulario (PATCH de `SetTransactionalAsync`), registro_editable (`keepEditable` del tema), cierre_automatico_formulario (`CloseRuleJson`) | `formularios` |
| Versiones y enlaces | guardar/listar/restaurar_version_formulario, listar_enlaces_formulario, revocar_enlace_formulario | `formularios` |
| Campo (PATCH sobre la pregunta) | campo_bloqueado, campo_oculto, valor_por_defecto_dinamico, permisos_campo_por_rol (`FieldVisibilityJson` hide/readonly por rol), subformulario_de_campo | `formularios` |

Decisiones de detalle:
- Los parametros se validan contra el descriptor del verbo ANTES de guardar (nombre conocido, numero, si/no, opcion
  estatica, y codigos de campo contra el formulario); los obligatorios los valida el servicio.
- **Referencias por id**: `subcategoriaId` (codigo o nombre del concepto), `activityTypeId` (nombre) y
  `assigneeUserId` (correo) se traducen a Guid si no lo son; sin coincidencia, error (nunca se guarda una referencia
  rota). `listar_conceptos` ahora devuelve `concepto_id`.
- Las reglas creadas con `formulario` van a su documento `FRMRULES-<codigo>` (el mismo del constructor, activo); sin
  formulario, a "Reglas creadas por IA" (`IA-MCP`, activo). Nacen ACTIVAS salvo estado=desarrollo.
- Hallazgos documentados en el cerebro: `NOTIFICAR` solo registra (no envia); el boton de regla es `Button` (no
  `ActionButton`).

Cerebro de formularios: `docs/vault-mcp-formularios/` (12 notas, FO11 generada del codigo). El generador quedo
generico: `gen_referencia_codigo.py --tema flujos|formularios` (entiende el helper `Obj(...)`).

Catalogo del MCP de tableros: **187 -> 218**.

### Ola 3 - agente de IA del paso (+14 herramientas, `TableroMcpToolset.FlujosAgente.cs`, permiso `flujos`)

opciones_agente_nodo y ver_agente_nodo (lectura); agente_nodo (asignar/reemplazar con autonomia autonomo|propone;
avisa si el agente esta apagado; no en inicio/fin) y quitar_agente_nodo (destructiva); recursos como PATCH sobre
`SetNodeAgentResourcesAsync` (que guarda el estado COMPLETO): instrucciones_agente_nodo, correo_agente_nodo,
web_agente_nodo (Colmena), whatsapp_agente_nodo (linea, plantilla de frio, variables, encabezado, minutos de frio),
voz_agente_nodo (agente de voz, linea, reintentos y ventana), canales_agente_nodo, fallo_agente_nodo (ruta = nombre de
la opcion); metadatos del nodo: seguimiento_agente_nodo (`StepSlaList.Build` + `PulseSendWindow`),
tope_preguntas_agente_nodo y traspaso_salida_agente_nodo (mapa BpmnElementId destino -> nota, mergeado por salida).
`ver_flujo` muestra `agente_ia` por paso (marca APAGADO). Lineas, clientes y agentes se resuelven por id o nombre.

Catalogo del MCP de tableros: **218 -> 232**. Receta en el cerebro de flujos: F12.

## Consecuencias

- Una IA puede dejar un proceso funcionando de punta a punta sin pantalla (salvo el agente del nodo, ola 3, y crear
  reglas nuevas, ola 2).
- El catalogo crece a 187: por eso cada tema tiene su cerebro y la guia general remite a el.
- Pruebas: `FlowConfig_NodoCompuertaHumana_Avisos_Formularios_CicloDeVida_ADR0211`,
  `GuiaMcp_PorTema_GeneralYFlujos_Independientes` (dual PG + SQL Server), conteos de catalogo, y
  `Update_parcial_conserva_bloqueo_subform_cascada_permisos_y_default_dinamico` (unitaria).
- Prueba ola 3: `FlowAgent_AsignarConfigurarSeguimientoYQuitar_ADR0211` (dual).
- Pruebas ola 2: `FormConfig_Reglas_Opciones_Versiones_Campos_ADR0211` y `McpCatalog_ConFormConfig_EsConsistente_YDespacha`
  (dual).
- Pendiente tras el deploy: importar y publicar los cerebros de flujos y de formularios en `/cerebros-plantilla` con
  su tema, regenerar la referencia de la guia general desde el catalogo vivo y republicarla.

## Addendum 2026-11-26 (sobre ADR-0212, v0.16.400)

- **Correccion**: `campo_bloqueado` fijaba `IsLocked`, que es el candado de LAYOUT del constructor (no impide editar).
  Ahora fija `FormQuestion.IsReadOnly` (ADR-0212): nadie edita el campo, tampoco en `/f/` ni sin rol.
- **`diseno_campo`**: estilo de UN control (`StyleJson`, esquema `FormFieldStyle`). MEZCLA las claves enviadas sobre
  el estilo actual, `quitar` devuelve claves al tema, `limpiar` lo borra; el servicio sanea (colores #hex, rangos).
- **`renombrar_campo`**: expone `RenameFieldCodeAsync`. Sin `aplicar` es SIMULACION (cambios + advertencias de
  referencias externas); con `aplicar=true` renombra en cascada en una transaccion. Marcada destructiva.
- La descripcion de `update_question` ya no dice que lo omitido vuelve al valor por defecto (es PATCH desde
  2dd4286e) y remite a `renombrar_campo` para cambiar el codigo.
- Catalogo **232 -> 234**. Cerebro de formularios: receta FO12 (diseno visual), FO09 (renombrar), FO08/FO10.
- Pruebas: `FormConfig_Reglas_Opciones_Versiones_Campos_ADR0211` y `McpCatalog_ConFormConfig_EsConsistente_YDespacha`
  ampliadas (dual).
