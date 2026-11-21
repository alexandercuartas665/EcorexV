# ADR-0196 - Call center: llamada IA del motor de acciones de contactos (resultado, reintento y preguntas al formulario del contacto)

- Estado: Aceptada
- Fecha: 2026-11-21
- Contexto: ADR-0056 (motor de acciones por filtro de contactos), ADR-0188 (eventos de entrega SendGrid),
  ADR-0190 (agente de voz en nodo de seguimiento de flujos; capa compartida Retell), ADR-0067 (formularios),
  formularios del tercero (TerceroFormLink, referencia "TERCERO:{id}")

## Contexto

El cargador de contactos ya programa acciones por filtro (WhatsApp, correo, llamada). El paso "Llamada" en modo
IA colocaba una llamada con Retell, pero el usuario no veia que habia pasado (contesto, buzon, audio, transcripcion),
no habia reintento si el contacto no contestaba, no se podia elegir la linea de voz y, sobre todo, la llamada no
devolvia nada util al contacto: Retell no sabia que datos extraer y lo poco que volvia caia en un formulario
suelto ("voz:{callId}") sin relacion con el contacto.

El objetivo del negocio: programar llamadas de perfilamiento/actualizacion de datos, recordatorios de cartera o de
servicios, y que las respuestas queden en el contacto.

Restriccion de reparto (acordada con la sesion de flujos, ECOREX.voz): el call center NO ejecuta flujos de trabajo.
La voz DENTRO de los flujos (nodos de agente, reanudar pasos, reintentos de nodo, bitacora) es de ADR-0190. Solo
se coordina la capa compartida de conectividad (RetellWebhookProcessor, RetellVoiceService, VoiceCall,
RetellVoiceLine, webhook).

## Decision

1. **Resultado visible** (`VoiceCallOutcomes`): estado + `disconnection_reason` de Retell -> Llamando, En curso,
   Contesto, No contesto, Ocupado, Buzon, Rechazada, Fallo. "Sin contacto" reutiliza
   `RetellVoiceService.IsNoContactReason` (fuente unica con los flujos). "Ultimas ejecuciones" muestra el resultado,
   la duracion y, al hacer clic, resumen, grabacion (audio) y transcripcion. El run solo queda Fallido si la
   llamada fallo; no contestar no es fallo.
2. **Reintento si no contesta** (por paso): 0..5 reintentos, cada 5 min..4 h, siempre dentro de la ventana de horario
   del paso, sobre la MISMA fila del run (Attempts, ExternalRef = ultima llamada).
3. **Linea de voz por paso** (`ContactWorkflowCallParams.LineaVozId`; vacio = la por defecto).
4. **Respaldo del webhook**: `VoiceCallSyncService` (lo llama `ContactWorkflowWorker` cada minuto) consulta
   GET get-call a Retell las llamadas sin resultado (gracia 2 min, horizonte 6 h, tope 20) y aplica los mismos
   efectos del webhook. El webhook_url ya se envia en cada llamada; /config-voz lo muestra.
5. **Orden de eventos**: un evento anterior al estado previo (call_ended tardio sobre una analizada) se ignora;
   call_analyzed siempre aplica sus efectos (son idempotentes).
6. **Preguntas al formulario del contacto** (`ContactCallFormQuestions`): los campos de los formularios del paso
   son las preguntas. El dispatcher anexa a la instruccion un bloque "preguntas que debes hacer" y pide a Retell
   un campo de analisis por pregunta (enum con etiquetas, boolean, number, string). Al llegar call_analyzed, SOLO
   si la VoiceCall trae `ContactWorkflowRunId`, se vuelca a "TERCERO:{id}": whitelist dura, etiqueta -> id de
   opcion (sin mayusculas/tildes), vacios y opciones que no casan se omiten, y se MEZCLA con lo previo. Las llamadas
   de flujo siguen por "voz:{callId}" sin cambios. Al guardar el paso, el formulario se ofrece en la ficha del
   contacto (TerceroFormLink).

## Consecuencias

- Migracion dual `AddVoiceCallLastSynced` (voice_calls.last_synced_at_utc, nullable). Sin otras columnas.
- Una linea en modo simple (agent_id de Retell ya armado) no recibe el guion ni los campos de analisis: deben
  configurarse en Retell con los mismos codigos del formulario (la pantalla lo avisa).
- Se recomienda formularios "Registro editable" para el contacto: una respuesta enviada de un formulario no
  editable no se toca (SaveAsync la rechaza).
- Pendiente: plantilla de perfilamiento en produccion y validacion con llamada real; casos de cartera y servicios.
- Pruebas: VoiceCallOutcomesTests, ContactCallFormQuestionsTests, RetellWebhookIdempotencyTests,
  RetellContactFormDumpTests y escenarios de Llamada IA en ContactWorkflowDispatcherTests.
