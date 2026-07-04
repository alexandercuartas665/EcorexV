# ADR-0022: Editor de flujos con canvas propio fiel al prototipo (pantalla flujos)

- Estado: aceptada
- Fecha: 2026-07-04
- Relacionada con: ADR-0014 (workflow engine BPMN), ADR-0015 (dynamic forms),
  ADR-0016 (rules engine), ADR-0019 (E2E), ADR-0021 (constructor de formularios)

## Contexto

El modulo Flujos del proceso (000291) tenia el WorkflowEngine FUNCIONAL (ADR-0014:
ImportBpmnAsync/PublishAsync/StartInstanceAsync/CompleteStepAsync, versionado
inmutable, ciclos por RestartNodeId) pero la pagina /flujos era un stub. El prototipo
maestro (ECOREX.dc.html, pantalla 'flujos' + estado flowEditorOpen) define un INDICE
con 4 KPIs, busqueda, tabs de filtro por cargo y tarjetas con metricas, y un EDITOR
en modal grande con canvas PROPIO: toolbar flotante, nodos absolutos por tipo
(circulo/diamante/rectangulo), aristas SVG ortogonales con flechas, caja de stats con
hint contextual y panel derecho con 6 acordeones + "Saltar a otro flujo".

## Decisiones

### 1. Canvas propio del prototipo en vez de bpmn-js

El prototipo NO usa bpmn-js: trae su editor canvas (900x540, fondo de puntos, drag
con mouse, herramientas sel/conn/task/event/gw/del). Se replica ese editor en Blazor
Server puro (pointer events + SVG, cero librerias JS; el unico interop es el
portapapeles del boton "Copiar JSON"). Regla firme del proyecto: frontend 100% .NET
y fidelidad al prototipo por encima de la conveniencia tecnica.

### 2. El XML BPMN se REGENERA con DI para conservar la portabilidad del ADR-0014

ADR-0014 prometia round-trip con bpmn.io guardando el XML "tal cual". Con editor
propio esa promesa cambia de mecanica, no de fondo:

- `WorkflowNode` gana el layout del canvas (`X`, `Y`, `W?`, `H?`), llenado al importar
  desde `bpmndi:BPMNShape/dc:Bounds` (BpmnProcessParser extendido) o por
  `WorkflowAutoLayout` (BFS determinista) si el XML no trae DI.
- Cada mutacion del grafo (agregar/mover/renombrar/conectar/borrar/condicion)
  REGENERA `BpmnXml` completo con `BpmnXmlWriter`: bpmn:process + bpmndi con las
  coordenadas actuales, condiciones como `bpmn:conditionExpression` estandar.
- El contrato lo protege el test de ROUND-TRIP (`BpmnXmlWriterTests`):
  `Parse(Write(grafo))` reproduce ids, nombres, tipos, condiciones y coordenadas; una
  segunda vuelta por el writer produce el MISMO XML. Ademas un test de integracion
  reimporta por el motor el XML regenerado y obtiene el mismo grafo.
- El XML IMPORTADO se sigue guardando tal cual llego; solo el editor lo regenera.

### 3. Edicion solo en borradores (el versionado del motor es el camino)

El grafo de una definicion PUBLICADA nunca se muta (hay instancias ancladas a esa
version). `IWorkflowDesignService` aplica la regla en cada mutacion y ofrece
`EnsureDraftAsync`: si la definicion esta publicada, crea (o REUTILIZA, para no
inflar versiones) la version borrador siguiente por el camino existente del motor
(`ImportBpmnAsync` = max+1 no publicada) y copia lo que no viaja en el XML: Category,
RestartNodeId, AllowsAssignment y vinculos WorkflowNodeForm/WorkflowNodeRule
(mapeados por BpmnElementId). Los vinculos por nodo (formulario/reglas) SI se
permiten sobre publicadas: no forman parte del XML ni del grafo (mismo criterio que
el modulo de reglas 000802). Las propiedades (nombre/categoria/descripcion) tambien
son metadata editable en cualquier estado no archivado.

### 4. Estados del indice y pausa

- `WorkflowDefinition.IsPaused` (nuevo, default false): En marcha = IsPublished &&
  !IsPaused; Pausado = IsPublished && IsPaused; Borrador = !IsPublished.
- `StartInstanceAsync` rechaza (Invalid) instancias nuevas de definiciones pausadas;
  las que ya corren siguen su curso. Cubierto por test de integracion dual.
- Publicar se hace desde el editor (boton del header) con el `PublishAsync` existente.

### 5. Formula de metricas del indice (documentada en FlowCardDto)

Una tarjeta por ProcessCode (la version publicada o, si no hay, la mas reciente);
las metricas agregan TODAS las versiones del proceso:

- en marcha = instancias con Status = Running.
- ejecuciones (mes) = instancias con StartedAt dentro del mes calendario UTC en curso
  (deuda: zona horaria del tenant cuando exista en el modelo).
- exito = Completed / (Completed + Stuck + Cancelled) en % redondeado; las Running no
  cuentan y sin instancias terminadas el valor es 0 (igual que el prototipo muestra
  0% en borradores).

### 6. Export / import JSON (formato del prototipo)

Export produce el JSON del prototipo (id/nombre/categoria/estado/descripcion +
nodos con layout + conexiones con condicion); import lo convierte a XML via
`BpmnXmlWriter` y entra por `ImportBpmnAsync` (validaciones del motor incluidas),
creando SIEMPRE una definicion en Borrador (version max+1 si el codigo existe).
Acepta los alias de tipo del prototipo (start/end/gw/task) ademas de los nombres
BPMN, y aplica auto-layout si faltan coordenadas.

### 7. Migracion unica dual `AddWorkflowEditorFields`

Aditiva: `workflow_definitions` += category (varchar 100 null), is_paused (bool
default false); `workflow_nodes` += x, y (int default 0), w, h (int null). Aplicada
y verificada en PostgreSQL (5442) y SQL Server (1443). El seeder hace backfill:
definiciones con todos los nodos en (0,0) reciben auto-layout + XML regenerado con
DI (COT-COM ademas recibe categoria "Comercial"), y se siembran un borrador demo
("Mantenimiento y soporte", construido con el propio IWorkflowDesignService) y una
definicion publicada PAUSADA ("Visita tecnica de instalacion", VIS-TEC).

## Deudas explicitas (TODO)

- Asignar usuarios por nodo: placeholder legible "TODO cargo/ACL por nodo
  (PERMISO_CARGO del vault)"; el modelo llega con dependencias (000850). No se
  inflo la migracion con un AssigneesJson especulativo.
- Reglas de notificacion por nodo: chips ilustrativos; motor de notificaciones
  pendiente.
- "Saltar a otro flujo": modal funcional de seleccion, pero el vinculo real entre
  flujos (call activity / subprocess) no existe en el motor todavia.
- Borrar el ultimo endEvent deja un borrador no importable hasta agregar otro (el
  motor valida al importar/publicar... la validacion dura es solo del startEvent,
  que nunca se borra).
- Ejecuciones (mes) usa mes calendario UTC, no la TZ del tenant.

## Consecuencias

- /flujos queda FUNCIONAL de punta a punta contra datos reales (indice con metricas,
  editor con persistencia inmediata de grafo y layout, publicacion, pausa,
  export/import) sin tocar la semantica de ejecucion del motor.
- Cualquier XML producido por el editor abre en bpmn.io con el mismo diagrama
  (bpmndi generado) y cualquier BPMN 2.0 estandar del subconjunto soportado se
  importa con su diagrama o con auto-layout.
- Cobertura: unit (round-trip writer + parser DI + auto-layout), integracion dual
  (pausa bloquea StartInstance, startEvent protegido, mutaciones solo en borrador,
  EnsureDraft reutiliza versionado, editor persiste y regenera XML importable,
  export/import JSON, metricas del indice) y E2E Playwright (indice -> nuevo flujo
  -> agregar tarea -> conectar -> renombrar -> guardar -> reabrir y verificar).
