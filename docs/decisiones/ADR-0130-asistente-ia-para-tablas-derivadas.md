# ADR-0130: asistente IA conversacional para crear tablas derivadas (fase 2 de ADR-0127)

Fecha: 2026-10-22
Estado: Aceptada

## Contexto

ADR-0127 dejo el wizard determinista "Derivar tabla" (nombre -> agrupar -> agregados -> filtro -> vista previa
-> refresco -> crear). Funciona, pero exige que el usuario sepa de antemano que columnas agrupan y que
agregados quiere. El pedido de fase 2 es poder DESCRIBIR la tabla en lenguaje natural ("items por categoria
y marca con total de precio") y que un asistente la proponga, muestre la vista previa y la cree tras una
confirmacion humana. Ya existe un patron asi en la plataforma: el asistente de formularios
(`FormBuilderChatPanel` + `IFormBuilderChatService`), donde el modelo propone tool-calls y el humano las
Confirma/Rechaza antes de ejecutarlas. Ese servicio esta acoplado a formularios (store en BD por formulario,
snapshots, verify_form forzado, adjuntos) y pesa 850 lineas.

## Decision

1. **Motor generico "proponer y confirmar"** (`Ecorex.Application/Tenancy/ProposalChatEngine.cs`): se
   EXTRAE la maquinaria del gate humano a un motor reutilizable que recibe un `ProposalChatSettings`
   (proveedor, clave, modelo, prompt de sistema, toolset, herramientas de solo lectura, fuente de consumo y
   un reescritor de argumentos) y opera sobre una `ProposalConversation` en memoria. Lecturas corren solas;
   cualquier lote con una herramienta mutante se detiene como PROPUESTA pendiente; escribir con una propuesta
   pendiente la descarta; Confirmar ejecuta en orden y continua; Rechazar deja constancia y continua. Tope de
   8 vueltas, timeout de 90 s por llamada, cupo duro del plan respetado y consumo en `AiUsageLog`.
   `FormBuilderChatService` NO se migra en este ADR (riesgo alto, cero ganancia funcional); queda como
   candidato a montarse sobre el motor cuando se toque de nuevo.
2. **Toolset propio** `DerivedTableToolset` (Application/Tenancy) con tres herramientas que hablan por NOMBRE
   de columna (el modelo no adivina GUIDs): `describir_tabla` (columnas con tipo, agregados posibles, total y
   muestra de 10 filas), `proponer_derivacion` (arma la `DerivationSpec`, la valida con `DerivedRowsBuilder.Plan`
   y devuelve la vista previa de los primeros 10 grupos via `IDerivedTableService.PreviewAsync`, sin crear
   nada) y `crear_tabla_derivada` (alta via `IDerivedTableService.CreateAsync`: misma transaccion y
   validaciones que el wizard). Count va SIEMPRE (como en el wizard). Un origen derivado se rechaza en el
   toolset ANTES de tocar el servicio (un solo nivel), y el servicio lo vuelve a rechazar. No se registra como
   `IAgentToolset` general: sus herramientas exigen el `contenedor_id` que inyecta el chat y no tienen sentido
   en un agente de WhatsApp.
3. **Servicio** `IDerivedTableChatService` (Application/DataContainers): abre una conversacion por contenedor
   (y tabla origen preseleccionada), arma el arnes con las tablas del contenedor y sus columnas tipadas (las
   derivadas marcadas como no-origen), elige el proveedor marcado "IA gestion de formularios" en Servidores de
   IA (la IA de autoria de la plataforma; Gemini como respaldo) y FUERZA `contenedor_id` en cada llamada a
   herramienta, asi el modelo no puede apuntar a otro contenedor (el filtro global ya impide otro tenant).
   Las conversaciones viven en memoria del scope (circuito Blazor): duran lo que dura el wizard abierto, igual
   que el estado del wizard; no se persisten (a diferencia del chat de formularios, que edita un artefacto
   que vive dias).
4. **UI** como componente aparte `DerivedTableChatPanel.razor` (Components/Shared/DataContainers), abierto con
   el boton "Asistente IA" del wizard como columna izquierda del mismo modal. Pinta la vista previa real como
   tabla cuando corre `proponer_derivacion`, la tarjeta de propuesta con la receta en lenguaje humano y los
   botones Confirmar/Rechazar. Al confirmar la creacion, el wizard hace el MISMO cierre que su boton "Crear":
   primer refresco por `IProcessRunner.RunNowAsync` (queda en la bitacora), resumen y `OnCreated` al lienzo.
   `ContenedorDatos.razor` no cambia.

## Consecuencias

- El usuario describe la tabla, ve la vista previa calculada por el motor real, confirma y la derivada queda
  creada exactamente como con el wizard (misma spec, mismo servicio, mismo refresco). El wizard sigue
  disponible para quien prefiera el camino determinista; ambos conviven en el mismo modal.
- Nuevo motor generico listo para otros casos de uso "proponer y confirmar" (reglas, conectores, menu) sin
  clonar 850 lineas cada vez.
- Consumo visible en el panel (tokens/costo) y en el dashboard de IA con fuente `derived-table-chat`.
- Limites: sin adjuntos (no aplican), sin persistencia del hilo, una tabla por peticion (regla del arnes).
- Robustez aprendida en la prueba real con Gemini 2.5 pro: el motor normaliza nombres de herramienta con
  prefijo inventado ("ecorex.proponer_derivacion" -> "proponer_derivacion"); el toolset resuelve la tabla por
  id, luego por nombre exacto y solo despues sin distinguir mayusculas si es unico ('Items' vs 'ITEMS'
  conviven en ALEGRA), y el arnes lista cada tabla con su id para que el modelo lo use.
- Pruebas: `DerivedTableToolsetTests` (argumentos invalidos, origen derivado rechazado, describir, propuesta
  con vista previa, alta delegada) y `DerivedTableChatServiceTests` (propuesta -> confirmacion -> tabla
  creada, rechazar, descartar al escribir, contenedor_id forzado, consumo registrado). Sin IA ni BD real.


## Revertido 2026-10-24

El asistente IA de tablas derivadas se ELIMINO a pedido del usuario (prefiere crear las derivadas a mano
con el wizard determinista). Se borraron el panel de chat, `DerivedTableChatService`, el toolset
`DerivedTableToolset`, el motor `ProposalChatEngine`, su registro en DI y sus pruebas. El resto de la
funcionalidad de tablas derivadas (ADR-0127) sigue vigente. Ver ADR-0127, "Extension 2026-10-24 (2)".
