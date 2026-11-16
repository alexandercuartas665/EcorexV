# ADR-0171: Freno por proveedor de IA sin cupo y tope por conversacion en el barrido del agente

**Estado:** Aceptado (v0.16.332; pendiente de deploy con OK del usuario)
**Fecha:** 2026-11-16
**Decisores:** usuario (dueno del producto), dev principal

## Contexto

Del 2026-11-15 20:58 UTC al 2026-11-16 ~13:40 UTC la llave de Gemini (unica de plataforma) llego a su tope
mensual de gasto: HTTP 429 RESOURCE_EXHAUSTED "exceeded its monthly spending cap". El "Agente Comercial" de
EPRING dejo 3.634 errores en `ai_agent_run_logs` en solo 16 conversaciones (~227 por conversacion).

Diagnostico (prod, solo lectura): el que re-disparaba era el **barrido de respaldo** de `AgentReplyDispatcher`
(cada 2 min, ADR de v0.16.193): reencola toda conversacion cuyo ultimo mensaje es entrante, asentado y de menos
de 3 dias. Como el agente nunca respondia, cada barrido corria `RunAsync` otra vez y dejaba 3 filas (Inbound,
Prompt, Error). Cualquier error PERMANENTE (llave invalida, cupo agotado) se reintentaba asi durante 3 dias.

## Decision

1. **Freno por proveedor (`IAiProviderQuotaGuard`, singleton en memoria)**: `AiProviderClient` clasifica el error
   (`AiQuotaErrors`: "spending cap", "insufficient_quota", "exceeded your current quota", "credit balance",
   "billing" con 429/402/400/403; NO el limite por minuto de Gemini, que es transitorio). Al detectarlo pausa
   el proveedor 30 min: toda llamada (chat, vision, herramientas; agentes, flujos, extractores) responde de una
   vez sin HTTP. Al vencer, la siguiente llamada sondea; si sigue sin cupo se pausa otra vez; si responde bien se
   cierra la caida. El 429 de cupo tampoco entra en la cadena de reintentos internos (1,2 s / 3 s / 6 s).
2. **Agente conversacional en silencio**: `AgentConversationService.RunAsync` sale sin escribir en la bitacora
   mientras el proveedor de su agente esta pausado. La conversacion sigue pendiente y el barrido la atiende cuando
   vuelve el cupo.
3. **Tope por conversacion en el barrido**: no se reencola una conversacion que ya tiene 3 Error en la bitacora
   posteriores a su ultimo entrante. Queda visible en /bitacora-agente y se reactiva cuando el cliente escribe.
4. **Un solo aviso**: `LogError` una vez por caida (no por llamada) y banner rojo en Servidores de IA con el
   proveedor, desde cuando, hasta cuando esta pausado y el detalle del proveedor.

## Opciones consideradas

| Opcion | Pros | Contras |
|---|---|---|
| Freno en el cliente por proveedor (elegida) | Cubre a todos los llamadores; un solo punto | Estado en memoria por proceso (un reinicio cuesta un sondeo) |
| Solo tope en el barrido | Cambio minimo | Los flujos/extractores seguirian golpeando al proveedor; cada conversacion nueva igual falla |
| Persistir el estado en BD | Sobrevive reinicios | Migracion y escritura por llamada para un caso raro |

## Consecuencias

- Durante una caida de cupo no se llama al proveedor ni se llenan las bitacoras; el aviso queda en Servidores de IA.
- La clave del freno es el proveedor (la llave es unica de plataforma). Si un dia hay llaves por tenant, habra que
  pasar a clave por configuracion.
- Una conversacion con 3 fallos tras el ultimo mensaje ya no se reintenta sola; un humano la ve en la bitacora.
