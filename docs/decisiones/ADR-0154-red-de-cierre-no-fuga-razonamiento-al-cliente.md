# ADR-0154: La red de seguridad de cierre no debe fugar razonamiento interno al cliente

**Status:** Accepted
**Date:** 2026-11-06
**Deciders:** Alexander Cuartas (dev principal), sesion data-ops AGROMETALICAS (reporte)

## Contexto

El runtime de agentes de IA (`AiInferenceService.RunToolLoopAsync`) tiene una "red de seguridad de
cierre": si el modelo AFIRMA que registro al cliente o que un asesor lo contactara pero NUNCA invoco
una herramienta de cierre, se le re-pincha una sola vez para forzar la herramienta, de modo que el
cliente no quede fuera del pipeline.

En produccion (tenant AGRO, agente SARA, conversacion con la clienta Betty Bejarano, tarea T00723,
2026-11-06 ~19:42 UTC) el cliente recibio por WhatsApp un texto INTERNO en ingles:

> "I have already registered the customer's request using the `crear_actividad` function as instructed
> by my primary prompt. ... creating a lead now would be incorrect and redundant..."

### Causa raiz

La red definia como unica herramienta de cierre `crear_lead`:

```csharp
var closeToolNames = new HashSet<string>{ "crear_lead" };
```

Flujo del caso:

1. El modelo llamo `crear_actividad` y creo T00723, pero `closeToolInvoked` quedo FALSE (no fue `crear_lead`).
2. El texto de cierre ("He registrado... un asesor te contactara") disparo `ClaimsCloseWithoutTool`.
3. Como `!closeToolInvoked`, la red re-pincho exigiendo `crear_lead`.
4. El `system_prompt` de SARA PROHIBE `crear_lead` (agente no comercial: registra con
   `crear_actividad`/`crear_tarea`, nunca lead, para no duplicar). El modelo quedo atrapado y respondio
   con una JUSTIFICACION en ingles mencionando el nombre de la funcion. Ese `completion.Text` se
   devolvio como `result.Text` y se envio al cliente.

Es una colision entre el fix de prompt no-comerciales y una red de cierre que solo reconocia `crear_lead`.
Ademas, el saneador de salida existente (`StripToolCallArtifacts`) solo borra artefactos con forma
`nombre(...)`; el nombre citado como palabra (sin parentesis) paso limpio al cliente.

## Decision

Dos cambios en `AiInferenceService`:

1. **Las tres herramientas de registro cuentan como cierre.** `CloseToolNames` = `crear_lead`,
   `crear_actividad`, `crear_tarea`. Si el modelo invoca cualquiera, la red NO re-pincha. El mensaje
   del re-pinchazo deja de nombrar `crear_lead` como unica opcion.
2. **El texto meta/razonamiento jamas llega al cliente.** En el punto de retorno del texto final:
   - si la red re-pincho y aun asi no se invoco herramienta de cierre (`closeNudged && !closeToolInvoked`),
     se suprime el texto del modelo (suele ser justificacion/razonamiento) y se usa un acuse neutro que
     NO promete falsamente el registro;
   - si el texto final menciona el NOMBRE de cualquier herramienta registrada del agente
     (`ContainsToolName`, derivado de las herramientas reales, no de una lista fija), se trata como fuga
     por construccion y se suprime, recuperando el ultimo texto valido del cliente si lo hay.
   El texto fugado queda en la bitacora de debug ("Red de cierre: fuga suprimida"), no se pierde la
   trazabilidad.

## Opciones consideradas

### Opcion A: solo endurecer el system_prompt de SARA (config)
"Nunca enviar texto meta ni nombres de funciones; responder solo en espanol." Reduce el dano pero NO
elimina el re-pinchazo: mientras la red no reconozca `crear_actividad`, seguira generando un segundo
turno en cada cierre (posible mensaje duplicado). Mitigacion temporal, no el fix.

### Opcion B (elegida): corregir el codigo (closeToolNames + blindaje de salida)
| Dimension | Evaluacion |
|-----------|------------|
| Complejidad | Baja (set + guardas en el retorno; sin migracion) |
| Riesgo de regresion | Bajo (el caso que la red cubre sigue forzando la creacion real) |
| Cobertura | Alta (cubre cualquier toolset, no solo SARA) |

## Consecuencias

- Un agente operativo que registra con `crear_actividad`/`crear_tarea` ya no es re-pinchado hacia
  `crear_lead`: el cliente recibe UN solo mensaje de cierre, en su idioma, sin texto interno.
- Si por diseno la red aun se dispara (afirmacion de cierre sin crear nada), sigue forzando la
  herramienta; y si el modelo insiste en responder solo texto, ese texto meta no llega al cliente.
- El guard de nombres de herramienta es defensa en profundidad adicional al `StripToolCallArtifacts`
  existente (que solo cubre la forma `nombre(...)`).
- `ContainsToolName`, `SafeClientText`, `ClaimsCloseWithoutTool` y `CloseToolNames` quedan `internal`
  con pruebas unitarias (`AiCloseSafetyNetTests`).

## Mitigacion de config (AGRO, opcional)

Endurecer el `system_prompt` de SARA (Opcion A) como cinturon adicional. Util pero secundario: el fix
real es el de codigo.

## Action Items

1. [x] `closeToolNames` -> `CloseToolNames` con las tres herramientas de registro.
2. [x] Blindaje del texto de salida (supresion + acuse neutro + bitacora).
3. [x] Tests unitarios de la logica de decision.
4. [ ] Deploy a prod: pendiente del OK por version del usuario.
5. [ ] (AGRO) endurecer el system_prompt de SARA como mitigacion adicional.
