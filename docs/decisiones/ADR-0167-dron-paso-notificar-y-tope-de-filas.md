# ADR-0167 - Dron: paso "Notificar" por canal con plantilla y tope de filas para simular

- Estado: Aceptado (implementado en rama `feat/agentes-sistema`, SIN deploy)
- Fecha: 2026-11-13
- Relacionado: ADR-0166 (Crear actividad), ADR-0140 (aviso de tarea asignada del agente), avisos de nodo (NodeNotifyService)

## Contexto

El DRON PLATAFORMAS INGENIOS ya crea una tarea por pedido nuevo con su formulario (ADR-0166). El usuario pidio un
paso mas en el extractor: avisar a un usuario que la tarea esta creada, con el mensaje armado desde una plantilla y
eligiendo el canal. Ademas, como el portal no tiene pedidos pendientes, quiere seguir simulando la carga.

Hallazgos:
- Crear la tarea con responsable ya dispara un aviso in-app + correo basico (TaskItemService), y el concepto puede
  tener destinatarios fijos; ambos con texto fijo, sin plantilla y sin WhatsApp/Telegram.
- Ya existen las piezas: `INotificationChannelSender` (correo, plantilla HSM de WhatsApp, grupo de WhatsApp,
  Telegram), `INotifyTokenResolver` (tokens de la tarea: {tarea.numero}, {form.<campo>}, {usuario.nombre}...),
  `INotifyLinkBuilder` (enlace a la tarea) e `INotificationService` (campana). Las usan los avisos de nodo y del
  agente, pero su logica de despacho es privada y atada a nodo/agente.
- El resolvedor de tokens solo lee el formulario marcado como PRINCIPAL de la tarea (`FormResponse.IsActive`), y el
  borrador que sembraba "Crear actividad" nacia sin esa marca -> {form.petof} salia vacio.

## Decision

1. Nuevo `ScrapeStepKind.Notificar = 23` (server-side, config en `MappingJson`, sin migracion), en `ScrapeNotificar`.
   - Canal: Campana, Correo, WhatsApp (plantilla aprobada + variables), Grupo de WhatsApp, Telegram.
   - Destino: responsable de la tarea, usuarios elegidos o destinatarios del concepto.
   - Mensaje/asunto: plantilla con los tokens de la tarea y tambien rutas del dron ({{DETALLE.cabecera.x}});
     `{tarea.enlace}` agrega el enlace directo.
   - Por defecto solo si la tarea se creo en esta corrida: "Crear actividad" deja `{{TAREA}}` y `{{TAREA_NUEVA}}`
     en el contexto, asi re-correr el dron no repite avisos.
   - Best-effort: un fallo de envio no corta el bucle (queda en la bitacora de la tarea); una config invalida si.
2. "Crear actividad" marca su formulario como principal de la tarea (`SetActiveTaskFormAsync`).
3. Tope de filas por bucle con la variable del flujo `TopeFilasBucle` (como la ya existente `RutaDescargaBot`),
   para simular/probar con pocos registros sin migracion.
4. Simulacion (solo config local): flujo "DRON PLATAFORMAS INGENIOS - SIMULACION" que toma pedidos del historico
   como si fueran nuevos (tope 2) y pasa por Crear actividad + Notificar.

## Consecuencias

- Reusa el mismo despachador y tokens que los avisos de nodo: misma semantica de plantillas de WhatsApp.
- Corre sin usuario actor (Guid.Empty), igual que otros envios de fondo; solo afecta la auditoria del envio.
- Validado E2E en local: simulacion crea 2 tareas y 2 avisos por campana con los datos del pedido; segunda corrida
  no duplica tareas ni avisos. Tests: SuperAdmin 224/224, integracion dual PG + SQL Server 12/12.
- Para prod: el paso y la variable son config por tenant (no viajan con el codigo); WhatsApp exige una linea y una
  plantilla aprobada del tenant.
