# ADR-0001: Puertos Docker locales reasignados

**Fecha:** 2026-05-19
**Estado:** REEMPLAZADO por ADR-0010 (2026-07-03, bloque dedicado ECOREX Tareas)
**Contexto del proyecto:** heredado del backbone CUBOT.nails - scaffolding inicial

## Contexto

La maquina de desarrollo (`C:\DesarrolloIA\ECOREX.tareas`) ya tiene una pila Docker activa (`propia-*`) que ocupa los puertos host estandar de la hoja de ruta:

- `propia-pgadmin` -> 5050
- `propia-postgres` -> 5433
- `propia-redis` -> 6380

El usuario pidio explicitamente "valida puertos antes de crear los nuevos servicios" y "no hagas ajustes al sistema operativo". Eso descarta detener la pila existente o liberar puertos a la fuerza.

## Decision

Asignar a ECOREX.tareas un rango de puertos hermano de la pila `propia-*` para mantener simetria y evitar colisiones:

| Servicio | Hoja de ruta | Asignado |
|----------|--------------|----------|
| PostgreSQL | 5432 | **5434** |
| Redis | 6379 | **6381** |
| RabbitMQ AMQP | 5672 | **5673** |
| RabbitMQ Management | 15672 | **15673** |
| pgAdmin | 5050 | **5051** |

Los puertos internos de los contenedores siguen siendo los estandar (5432, 6379, 5672, 15672, 80). Solo cambia el mapeo host->contenedor en `docker-compose.yml`.

## Consecuencias

- Las cadenas de conexion de las aplicaciones .NET locales deben usar 5434, 6381, 5673.
- Snippets y prompts de la hoja de ruta que asumen 5432/6379/5672 deben adaptarse cuando se ejecuten contra el entorno local.
- En produccion, staging y CI/CD se conserva el puerto estandar (los puertos son cosa del host local, no del servicio).
- Si en algun momento se libera la pila `propia-*`, se puede revisitar volver a los puertos estandar.

## Validacion

```powershell
Test-NetConnection localhost -Port 5434
Test-NetConnection localhost -Port 6381
Test-NetConnection localhost -Port 5673
Test-NetConnection localhost -Port 15673
Test-NetConnection localhost -Port 5051
```
