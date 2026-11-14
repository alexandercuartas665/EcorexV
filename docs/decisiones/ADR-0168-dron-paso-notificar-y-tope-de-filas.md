# ADR-0168 - Dron: paso "Notificar" por canal con plantilla y tope de filas para simular

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

## Addendum 2026-11-14 - Numeros de WhatsApp sueltos e import de plantillas

- Notificar admite `Telefonos` (lista libre, un numero por linea en el editor) para el canal WhatsApp: se SUMAN a
  los destinatarios del destino elegido sin repetir, o son los unicos con el destino nuevo `Destino.Numeros`
  ("Solo numeros de WhatsApp"). Se normalizan a E.164 (10 digitos que empiezan por 3 -> +57). Sin migracion.
- "Importar desde YCloud" (la unica reconciliacion del estado de aprobacion; SyncStatus sigue en stub y no hay
  webhook de estado de plantillas) pisaba el cuerpo y las variables con NOMBRE de una plantilla creada en el sistema
  por las posicionales de Meta ({{1}}..), y los emisores que arman los parametros por nombre quedaban vacios (el guard
  de parametros vacios bloqueaba el envio). Ahora, si el cuerpo local compilado a posicionales coincide con el de Meta,
  se conservan cuerpo y variables con nombre y solo se reconcilian estado, id, categoria, header y footer
  (`WhatsAppTemplateComponents.HasFriendlyTokensFor`). Si el cuerpo cambio de verdad en Meta, gana el de Meta.
- Config local: plantilla `aviso_nuevo_pedido_plataforma` (UTILITY, es; variables pedido/solicitud/tarea) sometida
  en la linea YCloud de SOLDARCO; paso "Avisar nuevo pedido por WhatsApp" (order 102536) en el DRON real y 105559 en
  la simulacion. Tests: SuperAdmin 233/233, Application 1386/1386, integracion dual 26/26.