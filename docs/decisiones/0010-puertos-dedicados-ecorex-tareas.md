# ADR-0010: Bloque de puertos Docker dedicado para ECOREX Tareas

**Fecha:** 2026-07-03
**Estado:** Aceptado (reemplaza a ADR-0001)
**Contexto del proyecto:** ECOREX.tareas - FASE 0 (clon del backbone CUBOT.nails)

## Contexto

Este repo se clono del backbone CUBOT.nails, que traia los puertos del ADR-0001
(5434/6381/5673/15673/5051). La maquina de desarrollo corre en paralelo varios
stacks Docker (doktrino, cubot, cubot-travels, cubotrm, visal, propia-*), y la
HOJA DE RUTA del vault OBSIDIAN.tareas asigna a ECOREX Tareas un bloque dedicado,
verificado libre el 2026-07-03. Ademas el proyecto exige DAL dual, lo que agrega
SQL Server 2022 a la pila local (el backbone era Postgres-only).

## Decision

| Servicio | Estandar | Dedicado ECOREX Tareas |
|----------|----------|------------------------|
| PostgreSQL 16 | 5432 | **5442** |
| SQL Server 2022 (nuevo) | 1433 | **1443** |
| Redis | 6379 | **6389** |
| RabbitMQ AMQP | 5672 | **5682** |
| RabbitMQ Management | 15672 | **15682** |
| Adminer (reemplaza pgAdmin) | 8080 | **8092** |

- Project name de compose: `ecorex-tareas`; contenedores, volumenes y red con
  prefijo `ecorex-tareas-` para no colisionar con otros stacks.
- Adminer sustituye a pgAdmin porque administra Postgres Y SQL Server con una sola UI.
- `deploy/docker/preflight.ps1` (y `.sh`) corren ANTES de `docker compose up -d`:
  docker vivo, puertos libres, sin contenedores previos rotos, recursos minimos.
- Los puertos se parametrizan en `.env` (no versionado); `.env.example` documenta defaults.

## Consecuencias

- Cadenas de conexion locales usan 5442 (Postgres) y 1443 (SQL Server).
- ADR-0001 queda reemplazado; sus puertos eran del backbone, no de este proyecto.
- En produccion/staging/CI se conservan los puertos estandar de cada servicio.

## Validacion

```powershell
cd C:\DesarrolloIA\ECOREX.tareas\deploy\docker
.\preflight.ps1
```
