# ADR-0167: Recordatorios de turnos configurables (WhatsApp ahora, llamada con agente de voz despues)

**Estado:** Aceptado (etapa 1 WhatsApp en v0.16.317; etapa 2 llamada propuesta)
**Fecha:** 2026-11-13
**Relacionado:** ADR-0165 (Agendas: servicios, turnos, herramientas del agente), ADR-0102 (plantillas WhatsApp por
YCloud), ADR-0164 (envio registrado en Conversaciones), ADR-0056 / Capa 3a (llamadas de voz RetellAI + Telnyx).

## Contexto

Los turnos del modulo de Agendas se pierden si el cliente olvida la cita ("no asistio"). El usuario pidio un modulo
configurable de recordatorios: CUANDO se avisa (15 minutos, 1 hora, 1 dia antes...) y por QUE metodo (WhatsApp con
linea y plantilla, o llamada telefonica con un agente del sistema). Decisiones tomadas con el usuario:

- La respuesta del cliente ("confirmo", "no puedo") la atiende el AGENTE de la linea (si no hay agente, queda en
  Conversaciones para un humano).
- La llamada debe actualizar el turno segun su resultado (etapa 2).
- Se construye por etapas: WhatsApp primero.

## Decision

1. **Regla de recordatorio por tenant** (`AgendaRecordatorioRegla`): nombre, activa, servicios a los que aplica
   (vacio = todos), anticipacion en minutos, horario permitido opcional (hora local del tenant) con politica fuera de
   horario (Adelantar = al cierre del horario permitido anterior; Omitir), "solo si aun no confirmo" y canal.
   - WhatsApp: linea + plantilla de ESA linea + mapa variable-de-plantilla -> token del turno ({turno.cliente},
     {turno.servicio}, {turno.fecha}, {turno.hora}, {turno.recurso}, {turno.ticket}, {empresa.nombre}...). Las
     variables se sugieren por nombre. YCloud y Cloud API exigen plantilla APROBADA; Evolution y el emulador la
     convierten a texto (mismo camino de envio para todos los proveedores).
   - Llamada: la regla ya guarda agente del sistema, linea de voz y objetivo (sin migracion para la etapa 2); hoy no
     se deja guardar con ese canal.
2. **Envio unico** (`AgendaRecordatorioEnvio`, indice unico regla + turno + inicio del turno): se RESERVA (Pendiente)
   antes de enviar y luego queda Enviado / Fallido / Omitido con su motivo. Nunca se avisa dos veces el mismo horario;
   si el turno se reprograma, el nuevo horario genera su propio recordatorio. Un fallo NO se reintenta solo (no se le
   escribe dos veces al cliente); queda en la bitacora.
3. **Omisiones con motivo**: horario no permitido (Omitir), turno ya empezado, turno reservado o reprogramado despues
   del momento del aviso (`AgendaTurno.HorarioDesdeUtc`), o "solo si no confirmo" con el turno confirmado.
4. **El mensaje queda en la conversacion del cliente** (`IChatService.RecordOutboundAsync`) y en el historial del turno:
   si el cliente responde, el agente de la linea ve el recordatorio como contexto. Nueva herramienta del agente
   `confirmar_turno` (las demas ya existian: turnos_del_cliente, reprogramar_turno, cancelar_turno); cuenta como cierre.
5. **Worker** `AgendaRecordatorioWorker` cada minuto (dentro de SuperAdmin como los demas; respeta
   ECOREX_DISABLE_WORKERS). La pantalla /agendas/recordatorios tiene "Procesar ahora" para probar y operar.
6. Menu: Agendas > Recordatorios (`agendas/recordatorios`, 001687). Migracion aditiva dual `AddAgendaRecordatorios`.

## Opciones consideradas

| Opcion | Pro | Contra |
|---|---|---|
| A. Reusar las reglas de notificacion por nodo (ADR-0100) | Ya existen | Estan atadas a pasos de flujo y a tareas, no a la hora de un turno |
| B. Recordatorio como paso de flujo del concepto | Sin modulo nuevo | Obliga a modelar un flujo por servicio; el "cuando" depende del turno, no del flujo |
| **C. Reglas propias de Agendas + worker (elegida)** | El "cuando" y el "como" se configuran en un solo sitio; reutiliza lineas, plantillas, chat y agente | Un modulo mas que configurar |

## Consecuencias

- Los clientes reciben el aviso por la linea y plantilla que elija cada empresa; si contestan, el agente confirma,
  reprograma o cancela sin intervencion humana.
- Etapa 2 (llamada): colocar la llamada con `IRetellVoiceService` (agente, linea de voz, variables del turno) y, con el
  resultado del webhook (call_analysis), confirmar o cancelar el turno. Requiere la linea de voz real para probar.

## Verificacion

- `AgendaRecordatorioCalculoTests` (unitarias): momento con horario permitido (adelantar / omitir), cada motivo de
  omision, tokens y sugerencias.
- `AgendaRecordatoriosTests` dual PG + SQL Server: validacion de la regla, envio con los datos del turno, una sola vez
  aun con varias pasadas, nuevo envio tras reprogramar, omitido por reserva tardia, "solo si no confirmo", fallo del
  proveedor con motivo, AISLAMIENTO cross-tenant. `AgendaTurnosTests`: el agente confirma con `confirmar_turno`.
- E2E local (5234, AGROMETALICAS, linea emulador): regla "5 dias antes" -> 3 enviados (texto real con fecha, hora y
  medica) y 2 fallidos por turnos sin telefono; el cliente responde "CONFIRMO" y SARA llama turnos_del_cliente +
  confirmar_turno: el turno queda Confirmado con el recordatorio y la confirmacion en su historial.
