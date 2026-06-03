# Como ejecutar CUBOT.nails en local

Estado a la fecha (secciones 1-7 de la hoja de ruta implementadas). Backend .NET 9,
Postgres/Redis/RabbitMQ/pgAdmin en Docker, y prototipo visual React (referencia).

## 1. Infraestructura (Docker)

```powershell
cd C:\DesarrolloIA\CUBOT.nails\deploy\docker
docker compose up -d
```

Puertos: Postgres 5434, Redis 6381, RabbitMQ 5673/15673, pgAdmin 5051. Ver ADR-0001.

Cadena de conexion local (no versionada):
`Host=localhost;Port=5434;Database=cubot_nails_dev;Username=cubot;Password=<.env>`

## 2. API (.NET) - autenticacion y endpoints Super Admin

La cadena de conexion se pasa por variable de entorno o argumento (no se versiona).

```powershell
$env:CUBOT_DB_CONNECTION = "Host=localhost;Port=5434;Database=cubot_nails_dev;Username=cubot;Password=TU_PASSWORD"
dotnet run --project apps\backend\src\CubotNails.Api\CubotNails.Api.csproj --launch-profile http
# Escucha en http://localhost:5280
```

En Development aplica migraciones y siembra datos automaticamente. Credenciales sembradas:

- Super Admin: `admin@cubot.nails` / `Admin123*`
- Admin agencia demo: `demo-admin@cubot.nails` / `Demo123*`

Ejemplos:

```powershell
# Login (devuelve JWT propio)
curl -X POST http://localhost:5280/connect/token -H "Content-Type: application/json" -d '{"email":"admin@cubot.nails","password":"Admin123*"}'
# Endpoints admin protegidos por SuperAdminOnly: /admin/tenants, /admin/plans, /admin/subscriptions, /admin/payments
```

## 3. Consola Super Admin (Blazor)

```powershell
$env:CUBOT_DB_CONNECTION = "Host=localhost;Port=5434;Database=cubot_nails_dev;Username=cubot;Password=TU_PASSWORD"
dotnet run --project apps\backend\src\CubotNails.SuperAdmin\CubotNails.SuperAdmin.csproj --launch-profile http
# http://localhost:5232  -> login con admin@cubot.nails / Admin123*
```

Paginas: Dashboard (metricas), Agencias (alta + activar/suspender), Planes (alta + listado).

> Nota: tambien se puede inyectar la conexion por argumento:
> `dotnet run --project ... -- --ConnectionStrings:Default="Host=...;Password=..."`.

## 4. Prototipo visual React (solo referencia)

Es el diseno objetivo de la experiencia (no es el producto; ver ADR-0004). Requiere bun.

```powershell
cd apps\web-prototype
bun install
bun run dev
# http://localhost:8080  (Dashboard, Pipeline, Conversaciones, Leads, Asesores,
#                         Lineas, Agentes, Automatizaciones, Metricas, Admin)
```

## 5. Pruebas

```powershell
cd apps\backend
dotnet test
# Requiere Docker (los tests de integracion usan Testcontainers.PostgreSql)
```

## Preview rapido (Claude Preview)

`.claude/launch.json` define configuraciones `superadmin`, `web` y `prototype`.
Para SuperAdmin se debe inyectar la conexion (no versionada) antes de previsualizar.
