# ADR-0163: SARA ve los casos del cliente, anota en el abierto y avisa al asignado del caso

**Estado:** Aceptado
**Fecha:** 2026-11-11
**Reemplaza parcialmente:** ADR-0101 rev.3 (Capa 2 de crear_tarea). Complementa ADR-0161.

## Contexto

Caso real en AGROMETALICAS (prod, conversacion 573217626882, 2026-11-11 17:03): el cliente tenia una
COTIZACION abierta (T00783) y escribio para radicar un RECLAMO. SARA llamo `crear_tarea` con titulo
"Reclamo por mala atencion", pero la Capa 2 de idempotencia (ADR-0101 rev.3: conversacion + tablero, SIN
ventana de tiempo) devolvio `idempotente: true` con el ticket de la cotizacion. Resultado:

- el reclamo NO se creo y SARA le dijo al cliente "tu reclamo quedo con el ticket T00783" (numero equivocado);
- el aviso de "tarea asignada" (va al asignado de la tarea) no se disparo, porque solo corre en creacion real;
- solo corrio la alerta de CIERRE, cuyo destino "Usuario asignado" resolvia el "dueno de la LINEA de WhatsApp"
  (concepto heredado del backbone, sin UI para configurarlo) y dejo en la bitacora un error enganoso:
  "Cierre: NO se pudo notificar ... (sin usuario). Motivo: la linea de la conversacion no tiene usuario asignado".

La regla de rev.3 no compara contenido a proposito (el modelo reescribe resumenes y alucina telefonos), asi que
el servidor no puede distinguir por si solo "tema distinto" de "repeticion del mismo cierre".

## Decision

1. **Quien decide caso nuevo vs viejo es el agente**, con dos herramientas nuevas en `TasksToolset`:
   - `casos_del_cliente` (solo lectura): ultimos 5 casos del cliente, de su contacto en el Directorio (telefono
     ultimos 10 digitos / identificacion) y de la conversacion. Incluye cerrados, marcados `abierto=false`, con
     su ultima anotacion.
   - `anotar_caso(numero, texto)`: agrega una anotacion (comentario de la tarea) a un caso ABIERTO del cliente.
     Rechaza casos cerrados/archivados (indica crear uno nuevo) y casos de otro cliente. Cuenta como cierre del
     turno (SessionCompleted) y se agrega a `CloseToolNames` (red de seguridad ADR-0154).
   - La descripcion de `crear_tarea` instruye consultar `casos_del_cliente` antes de crear.
2. **Capa 2 de crear_tarea = red de seguridad estrecha**: misma conversacion + mismo tablero + MISMO TITULO
   (normalizado) dentro de 30 min devuelve la tarea existente. Titulo distinto siempre crea. `crear_actividad`
   conserva la Capa 2 de rev.3 (no se toco su comportamiento).
3. **La alerta de cierre avisa al asignado del caso tocado**: `AiToolRunContext` registra los casos que el
   agente CREO o ANOTO en el turno. Con destino "Usuario asignado" / "Usuario de la tarea" la alerta va al
   `AssigneeTenantUserId` de cada caso tocado, con los tokens del caso ({tarea.*}, {usuario.*}, {tercero.*}).
   Sin casos tocados cae al dueno de la linea (compatibilidad) y, si tampoco hay, se registra como "omitida"
   (Info), no como error.
4. **Un solo mensaje por usuario y turno**: se mantiene el de-dup de ADR-0161 (si el aviso de tarea asignada ya
   le escribio, el cierre se omite) y ahora el propio cierre marca al usuario notificado.
5. **Bitacora legible**: el registro dice el caso ("Cierre del caso T00098: se notifico por WhatsApp a ...").

## Opciones consideradas

| Opcion | Pro | Contra |
|---|---|---|
| A. Deduplicar por contenido/similitud en el servidor | Sin depender del modelo | Ya fallo (ADR-0101): resumen y telefono no son estables |
| B. Parametro `es_caso_nuevo` en crear_tarea | Simple | El modelo decide a ciegas, sin ver los casos |
| **C. Herramientas para ver y anotar casos (elegida)** | El agente decide viendo los casos; los temas viejos quedan anotados, no duplicados | Depende de que el agente consulte; se mitiga con la descripcion de crear_tarea + red de 30 min |

## Consecuencias

- Un tema distinto ya no se pierde en un caso abierto ajeno; el cliente recibe el ticket correcto.
- Un cliente que vuelve sobre su caso abierto deja la anotacion en ese caso y su asignado es avisado.
- Las herramientas nuevas quedan habilitadas para todos los agentes que tienen el grupo "Tareas" (la ficha del
  agente usa lista de deshabilitadas); se pueden apagar por agente.
- Riesgo residual: si el agente NO consulta casos y repite un tema viejo con otro titulo, se crea un caso
  duplicado. Se refuerza indicandolo en el guion (prompt) del agente.
- No hay migracion: todo es codigo + contexto del turno.

## Verificacion

- Tests: `TasksToolsetBoardWhitelistTests` (regresion del reclamo, ventana de 30 min, casos del cliente, anotar
  abierto/cerrado/ajeno), `AgentCierreDedupTests` (casos tocados), `AiCloseSafetyNetTests`. Suite Application
  1323/1323 + nuevos.
- E2E en dev (5234, emulador de canal, BD local): cotizacion abierta + "tengo un reclamo" -> caso NUEVO T00098
  asignado por round-robin y "Cierre del caso T00098: se notifico por WhatsApp a <asignado>"; luego "como va mi
  reclamo, hoy volvi a llamar" -> `casos_del_cliente` + `anotar_caso(T00098)`, sin caso nuevo, y aviso al
  asignado.
