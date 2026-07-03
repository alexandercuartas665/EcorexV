# Infraestructura local de ECOREX.tareas

Pila Docker Compose para desarrollo local. Incluye PostgreSQL, SQL Server 2022 (DAL dual),
Redis, RabbitMQ y Adminer.

## Puertos asignados (bloque DEDICADO ECOREX Tareas)

La maquina corre varios stacks Docker en paralelo (doktrino, cubot, cubot-travels,
visal, propia-*). ECOREX Tareas usa un bloque de puertos propio y prefijo
`ecorex-tareas-` en contenedores, volumenes y red. Ver ADR-0010.

| Servicio | Puerto host | Puerto interno | Acceso |
|----------|-------------|----------------|--------|
| PostgreSQL 16 | 5442 | 5432 | `Host=localhost;Port=5442;Database=ecorex_dev;Username=ecorex;Password=...` |
| SQL Server 2022 | 1443 | 1433 | `Server=localhost,1443;Database=ecorex_dev;User Id=sa;Password=...;TrustServerCertificate=true` |
| Redis | 6389 | 6379 | `localhost:6389` (con password) |
| RabbitMQ AMQP | 5682 | 5672 | `amqp://ecorex:...@localhost:5682` |
| RabbitMQ Management UI | 15682 | 15672 | http://localhost:15682 |
| Adminer | 8092 | 8080 | http://localhost:8092 (sirve Postgres y SQL Server) |

## Levantar la pila (pre-flight primero)

```powershell
cd C:\DesarrolloIA\ECOREX.tareas\deploy\docker
.\preflight.ps1          # docker vivo, puertos libres, sin contenedores muertos, recursos
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
docker compose exec postgres pg_isready -U ecorex -d ecorex_dev
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $env:SQLSERVER_SA_PASSWORD -C -Q "SELECT 1"
docker compose exec redis redis-cli -a $env:REDIS_PASSWORD ping
docker compose exec rabbitmq rabbitmq-diagnostics ping
```

## Notas

- Las contrasenas reales viven en `deploy/docker/.env` (ignorado por git).
- `deploy/docker/.env.example` es la plantilla versionable.
- Los datos persisten en volumenes nombrados `ecorex-tareas-postgres-data`,
  `ecorex-tareas-sqlserver-data`, `ecorex-tareas-redis-data`, `ecorex-tareas-rabbitmq-data`.
- SQL Server necesita >= 2 GB de RAM asignada a Docker; el preflight lo verifica.
