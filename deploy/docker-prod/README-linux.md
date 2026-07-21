# Deploy de ECOREX.tareas en Linux (BUILD-FROM-GIT)

Flujo sin registry (GHCR) y sin subir codigo: el server clona el repo PUBLICO
`https://github.com/alexandercuartas665/EcorexV.git`, construye la imagen y
levanta el stack. Solo copias 2 archivos al server.

Convive con Visal (u otros stacks) en el MISMO Docker sin chocar: contenedores,
red y volumen de datos son propios de ECOREX y el volumen es PERSISTENTE.

- Contenedores: `ecorex-app`, `ecorex-postgres-prod`
- Volumen datos: `ecorex-prod_ecorex-pgdata` (persistente)
- Volumen subidas: `ecorex-prod_ecorex-uploads` (persistente, ver mas abajo)
- Red: `ecorex-prod_ecorex-net`
- Puerto local por defecto: `127.0.0.1:5480` (Visal usa 5380)

---

## Requisitos en el server

- Docker Engine 24+ con Compose v2.
- Salida HTTPS a `github.com:443`.
- ~3 GB libres para la imagen (.NET SDK + runtime + Chromium).

---

## Pasos (una sola vez)

1. **Crear la carpeta de deploy:**
   ```bash
   mkdir -p /opt/ecorex && cd /opt/ecorex
   ```

2. **Bajar los 2 archivos desde el repo:**
   ```bash
   BASE="https://raw.githubusercontent.com/alexandercuartas665/EcorexV/fase-0/clon-backbone/deploy/docker-prod"
   curl -fsSL "$BASE/docker-compose.from-git.yml" -o docker-compose.from-git.yml
   curl -fsSL "$BASE/.env.example"                 -o .env
   ```

3. **Editar `.env`** y cambiar, como minimo, las 3 claves marcadas CAMBIAR:
   ```bash
   nano .env
   #  POSTGRES_PASSWORD          -> openssl rand -base64 32
   #  ECOREX_SEED_ADMIN_PASSWORD -> clave del primer admin (admin@ecorex.local)
   #  ECOREX_PORT                -> 5480 (o uno libre: ss -tlnp | grep 5480)
   ```

4. **Build + up:**
   ```bash
   docker compose -f docker-compose.from-git.yml build      # 1a vez ~5-10 min
   docker compose -f docker-compose.from-git.yml up -d
   docker compose -f docker-compose.from-git.yml ps
   docker compose -f docker-compose.from-git.yml logs -f ecorex-app
   ```
   En los logs debes ver que aplica migraciones y crea el Super Admin.

5. **Comprobar que responde localmente** (desde el server):
   ```bash
   curl -I http://127.0.0.1:5480/login    # debe responder 200 con HTML del login
   ```

6. **Entrar la primera vez:** usuario `admin@ecorex.local`, clave = la que
   pusiste en `ECOREX_SEED_ADMIN_PASSWORD`. No hay datos demo en produccion.

---

## Ingreso: puerto plano (estado actual de 10.0.0.3)

El box `10.0.0.3` HOY no tiene reverse proxy ni TLS activo: cada app se expone
directo en su puerto (Visal 5380, bookstack 6875, ocsinventory 9090). ECOREX
sigue el mismo patron y queda en **`http://<IP-del-box>:5480/login`**.

- No hay paso extra: el `docker-compose.from-git.yml` ya publica `5480` en todas
  las interfaces del host.
- Abre `5480` en el firewall del server/proveedor si quieres alcanzarlo desde
  fuera de la red.

> Blazor Server usa SignalR sobre WebSockets; al ir directo al puerto no hay
> proxy que los pueda romper, asi que funciona sin config extra.

### (Opcional, a futuro) TLS con el Caddy incluido

Cuando quieras HTTPS con dominio, y como **80/443 estan libres** en este box,
puedes activar el Caddy incluido (carpeta `caddy/`):

```bash
mkdir -p caddy
BASE="https://raw.githubusercontent.com/alexandercuartas665/EcorexV/fase-0/clon-backbone/deploy/docker-prod"
curl -fsSL "$BASE/caddy/docker-compose.caddy.yml" -o caddy/docker-compose.caddy.yml
curl -fsSL "$BASE/caddy/Caddyfile"                -o caddy/Caddyfile
# Define ECOREX_DOMAIN en .env con un registro A al IP publico del box.
docker compose --env-file .env \
    -f docker-compose.from-git.yml -f caddy/docker-compose.caddy.yml \
    up -d
docker logs -f ecorex-caddy   # espera "certificate obtained successfully"
```

> Nota: este Caddy tomaria 80/443 solo para ECOREX. Si a futuro quieres un TLS
> unico para todas las apps del box (visal, bookstack...), eso es un proxy
> global aparte y lo decide el admin del server.

---

## Updates rutinarios

Tras un commit nuevo en la rama:
```bash
cd /opt/ecorex
docker compose -f docker-compose.from-git.yml build --no-cache   # reclona y reconstruye
docker compose -f docker-compose.from-git.yml up -d
docker compose -f docker-compose.from-git.yml logs --tail=50 ecorex-app
```
Para pinear una version concreta: en `.env` cambia `ECOREX_BRANCH` por un tag o
un commit sha y repite build + up.

> El `build --no-cache` + `up -d` **recrea el contenedor desde cero**. Todo lo
> que la app haya escrito en su sistema de archivos se pierde, salvo lo que este
> en un volumen. Por eso `wwwroot/uploads` esta montado (ver siguiente seccion).
> Nunca uses `docker compose down -v` en este stack: la `-v` borra los volumenes
> (BD **y** archivos subidos).

---

## Archivos subidos por los usuarios (volumen `ecorex-uploads`)

La app guarda los binarios que sube el usuario en el sistema de archivos, bajo
`wwwroot/uploads`; en la BD solo queda la ruta (`/uploads/items/...`). Incluye:

| Subcarpeta               | Contenido                              |
|--------------------------|----------------------------------------|
| `uploads/` (raiz)        | logo del tenant (`Cuenta`)             |
| `uploads/branding/`      | logo de marca de la plataforma         |
| `uploads/items/{tenant}/`| imagenes de items de inventario        |
| `uploads/avatars/`       | fotos de usuario                       |
| `uploads/chat/`          | adjuntos de conversaciones             |
| `uploads/leads/`         | archivos adjuntos de leads             |
| `uploads/cotizaciones/`  | PDFs de cotizaciones generados         |
| `uploads/agents/`        | recursos de agentes de IA              |
| `uploads/templates/`     | assets de plantillas                   |

Ese directorio esta en `.gitignore`, asi que **no lo respalda el repo**, y el
contenedor se recrea en cada deploy: **sin el volumen se pierde todo cada vez**.
El compose lo monta en `/app/wwwroot/uploads` (el `Dockerfile.superadmin` usa
`WORKDIR /app`, asi que el `wwwroot` publicado vive en `/app/wwwroot`).

Comprobar que quedo montado:
```bash
docker inspect -f '{{json .Mounts}}' ecorex-app | tr ',' '\n' | grep -i upload
docker exec ecorex-app ls -R /app/wwwroot/uploads | head
```

**Primer `up` tras agregar el volumen:** hay 17 archivos de demo versionados en
git dentro de `wwwroot/uploads` que viajan en la imagen. Docker copia el
contenido de la imagen dentro de un volumen con nombre **la primera vez que lo
crea vacio**, asi que esos 17 sobreviven y las imagenes actuales no se rompen.
Eso ocurre una sola vez: archivos nuevos que se agreguen al repo bajo
`wwwroot/uploads` NO llegaran a un volumen ya existente. Si hiciera falta:
```bash
docker cp <archivo> ecorex-app:/app/wwwroot/uploads/<subcarpeta>/
```

### Backup de los archivos

`backup.sh` respalda **solo Postgres**. Los binarios van aparte:
```bash
docker run --rm -v ecorex-prod_ecorex-uploads:/data -v "$PWD/backups":/out \
    alpine tar czf /out/ecorex-uploads-$(date +%F).tar.gz -C /data .
```

---

## Backups de Postgres

```bash
cd /opt/ecorex && ./backup.sh          # deja backups/ecorex-<fecha>.sql.gz
```
Programa diario con cron y sube los `.gz` a storage offsite.

Restaurar (BD vacia):
```bash
gunzip -c backups/ecorex-XXXX.sql.gz | docker exec -i ecorex-postgres-prod \
    psql -U ecorex -d ecorex
```

---

## Troubleshooting

- **`build` falla con `failed to clone`**: verifica salida a github.com desde el
  server (`curl -I https://github.com`). Si hay proxy corporativo, configura
  `HTTP_PROXY`/`HTTPS_PROXY` en el daemon de Docker.
- **`ecorex-app` reinicia en loop**: `docker compose ... logs ecorex-app`.
  Comunes: `POSTGRES_PASSWORD` con caracteres raros, o falta
  `ECOREX_SEED_ADMIN_PASSWORD`.
- **El sitio carga pero los clicks no responden**: WebSockets no habilitados en
  el reverse proxy. Habilitalos para el host de ECOREX.
- **Puerto 5480 ocupado**: cambia `ECOREX_PORT` en `.env` y `up -d` de nuevo.
- **Caddy no levanta (80/443 en uso)**: hay otro proxy en esos puertos. Usa la
  Opcion A (enrutar desde el proxy global).

---

## Notas

- Produccion corre **solo Postgres** (DAL dual, provider Postgres). Redis y
  RabbitMQ NO se incluyen: la consola funciona en instancia unica sin ellos
  (SignalR en memoria). Se agregarian si en el futuro se escala horizontalmente.
- Los secretos (llaves Wompi, API keys de IA) se cifran con DataProtection y su
  keyring vive en Postgres, asi que sobreviven reinicios del contenedor.
- El `.env` real NUNCA se versiona (esta en `.gitignore`).
