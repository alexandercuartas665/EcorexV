# ADR-0028: Infraestructura IA como grupo propio + desacople del cierre comercial

Fecha: 2026-07-05
Estado: Aceptado

## Contexto

ECOREX hereda del backbone CUBOT un stack COMPLETO de infraestructura de IA (agentes de IA,
lineas de WhatsApp, conversaciones del agente, bitacora del agente, plantillas de WhatsApp).
Hasta ahora esa infraestructura vivia oculta:

- La mayoria de sus paginas colgaba del grupo de menu "CRM (heredado)", mezclada con paginas
  genuinamente CRM (Asesores, Automatizaciones, Lista negra).
- La pagina "Agentes IA" colgaba del grupo "Automatizacion".

Ademas, el toolset de CIERRE del agente (`PipelineToolset`, herramienta `crear_lead`) dependia
DIRECTAMENTE del dominio CRM: inyectaba `ILeadService` y `IBusinessUnitService` y creaba el lead
en el pipeline. Es decir, el runtime de agentes de IA NO podia operar sin el CRM.

## Decision

### 1. Menu: grupo "Infraestructura IA" de primer nivel

Se extrae la infraestructura de IA a un grupo propio "Infraestructura IA" (data-acc `ia`), con 5
items, conservando rutas y codigos de modulo EXACTOS (solo es un movimiento de menu, no se
renombran rutas ni se crean paginas):

- Agentes (`/agentes`, modulo legacy 000867) — venia de "Automatizacion".
- Lineas WhatsApp (`/lineas`) — venia de "CRM (heredado)".
- Conversaciones (`/conversaciones`) — venia de "CRM (heredado)".
- Bitacora del agente (`/bitacora-agente`) — venia de "CRM (heredado)".
- Plantillas WhatsApp (`/plantillas-whatsapp`) — venia de "CRM (heredado)".

Consecuencias en el mapa de acordeones (`GroupRoutes`) y contadores de `NavMenu.razor`:

- "Automatizacion": contador 4 -> 3; se quita `agentes` de sus rutas.
- "CRM (heredado)": contador 7 -> 3; conserva SOLO items genuinamente CRM (Asesores,
  Automatizaciones, Lista negra). El grupo NO se elimina (aun tiene contenido real).
- Nuevo grupo `ia`: contador 5; rutas `agentes, lineas, conversaciones, bitacora-agente,
  plantillas-whatsapp`. Reusa el icono de automatizacion/bot ya presente en el archivo.

Nota de nomenclatura preservada: la pagina de agentes conserva su ruta `/agentes` (el spec
menciono rutas ideales como `/agentes`, `/lineas`, `/conversaciones`, `/bitacora-agente`, que
en la practica ya coinciden con las rutas existentes salvo el label "Agentes IA" -> "Agentes").

### 2. Costura: `IAgentLeadSink` desacopla el cierre del dominio CRM

Se introduce una costura (seam) para que el runtime de agentes NO dependa de Lead/CRM:

- Interfaz `IAgentLeadSink` (namespace `Ecorex.Application.Tenancy`, lado IA/agentes) con un solo
  metodo `CreateLeadAsync(AgentLeadRequest, Guid actor, ct)`. Los DTO `AgentLeadRequest` y
  `AgentLeadResult` viven en el namespace de IA y NO referencian la entidad Lead.
- `NoOpAgentLeadSink` (default): no crea nada, nunca lanza, devuelve un resultado tipado
  "no conectado" (`AgentLeadResult.NotWired`). Permite operar el agente sin CRM.
- `PipelineLeadSink` (adaptador CRM, implementacion VIVA): aqui, y SOLO aqui, vive el
  acoplamiento con `ILeadService`/`IBusinessUnitService`/pipeline. Conserva el comportamiento
  historico (mapeo de canal -> unidad de negocio + creacion del lead + nota del asesor).
- `PipelineToolset.crear_lead` ahora delega en `IAgentLeadSink`. NO cambia el contrato externo
  del tool: mismo nombre `crear_lead`, mismos argumentos JSON, mismo efecto observable. La
  resolucion del telefono por defecto (numero de la conversacion en curso) se mantiene en el
  toolset porque es contexto de la CONVERSACION del agente, no del CRM.

Registro en DI (`AddApplication`), el ultimo gana como implementacion viva:

```csharp
services.AddScoped<IAgentLeadSink, NoOpAgentLeadSink>();   // default documentado
services.AddScoped<IAgentLeadSink, PipelineLeadSink>();    // adaptador CRM VIVO
```

## Consecuencias

- El acoplamiento agente <-> CRM queda confinado a UNA clase adaptadora (`PipelineLeadSink`).
- Un despliegue sin CRM puede registrar solo el `NoOpAgentLeadSink` y el agente sigue operando.
- El comportamiento de creacion de leads end-to-end se preserva (la ruta viva es el adaptador).

## Sin migracion / esquema

Este cambio es SCHEMA-FREE: es una reorganizacion de menu + una costura de interfaz. No se
tocaron entidades, configuraciones EF ni el modelo; no se agrego ninguna migracion (ni PG ni
SQL Server). La condicion DAL dual se mantiene intacta.

## Pruebas

- Unitarias nuevas (`AgentLeadSinkTests`): (1) con `NoOpAgentLeadSink` el tool `crear_lead`
  responde OK y no crea ningun lead; (2) con `PipelineLeadSink` crea el lead y mapea la unidad de
  negocio por canal (b2b) exactamente como antes; (3) sin nombre devuelve error tipado sin tocar
  el sink.
- Integracion dual (PG + SQL Server): `PipelineLeadTests`, `FollowUpTaskTests`, `DashboardTests`
  siguen verdes (la creacion de leads via API pasa por el mismo `ILeadService`).
