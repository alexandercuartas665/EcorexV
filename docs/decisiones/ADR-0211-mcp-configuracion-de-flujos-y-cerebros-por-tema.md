# ADR-0211: MCP de tableros - configuracion completa de flujos (ola 1) y cerebros de guia por tema

- Estado: Aceptado (ola 1 en rama; olas 2 y 3 planeadas)
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

## Consecuencias

- Una IA puede dejar un proceso funcionando de punta a punta sin pantalla (salvo el agente del nodo, ola 3, y crear
  reglas nuevas, ola 2).
- El catalogo crece a 187: por eso cada tema tiene su cerebro y la guia general remite a el.
- Pruebas: `FlowConfig_NodoCompuertaHumana_Avisos_Formularios_CicloDeVida_ADR0211`,
  `GuiaMcp_PorTema_GeneralYFlujos_Independientes` (dual PG + SQL Server), conteos de catalogo, y
  `Update_parcial_conserva_bloqueo_subform_cascada_permisos_y_default_dinamico` (unitaria).
- Pendiente tras el deploy: importar y publicar el cerebro de flujos en `/cerebros-plantilla` con tema "flujos",
  regenerar la referencia de la guia general desde el catalogo vivo y republicarla.
