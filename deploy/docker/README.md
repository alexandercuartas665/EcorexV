# Infraestructura local de CUBOT.nails

Pila Docker Compose para desarrollo local. Incluye PostgreSQL, Redis, RabbitMQ y pgAdmin.

## Puertos asignados

Los puertos se eligieron para no chocar con otra pila Docker existente en la maquina (`propia-*` que ocupa 5050, 5433, 6380). Antes de cambiarlos validar con `Test-NetConnection`.

| Servicio | Puerto host | Puerto interno | Acceso |
|----------|-------------|----------------|--------|
| PostgreSQL | 5434 | 5432 | `Host=localhost;Port=5434;Database=cubot_nails_dev;Username=cubot;Password=...` |
| Redis | 6381 | 6379 | `localhost:6381` (con password) |
| RabbitMQ AMQP | 5673 | 5672 | `amqp://cubot:...@localhost:5673` |
| RabbitMQ Management UI | 15673 | 15672 | http://localhost:15673 |
| pgAdmin | 5051 | 80 | http://localhost:5051 |

## Levantar la pila

```powershell
cd C:\DesarrolloIA\CUBOT.nails\deploy\docker
docker compose up -d
docker compose ps
```

## Bajar la pila (mantiene datos)

```powershell
docker compose down
```

## Bajar y borrar datos

```powershell
docker compose down -v
```

## Validar conectividad

```powershell
docker compose exec postgres pg_isready -U cubot -d cubot_nails_dev
docker compose exec redis redis-cli -a $env:REDIS_PASSWORD ping
docker compose exec rabbitmq rabbitmq-diagnostics ping
```

## Notas

- Las contrasenas reales viven en `deploy/docker/.env` (ignorado por git).
- `deploy/docker/.env.example` es la plantilla versionable.
- Los datos persisten en volumenes nombrados `cubot-postgres-data`, `cubot-redis-data`, `cubot-rabbitmq-data`, `cubot-pgadmin-data`.
- Evolution API esta comentado en `docker-compose.yml` y se habilitara en una fase posterior segun la hoja de ruta.
