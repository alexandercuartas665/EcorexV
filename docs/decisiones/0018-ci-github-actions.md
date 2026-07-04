# ADR-0018: CI en GitHub Actions (pr-check) - FASE 7

- Estado: aceptada
- Fecha: 2026-07-03
- Relacionada con: ADR-0001 (DAL dual / matriz dual de tests), regla 6 de
  CLAUDE.md (repo publico, cero secretos versionados)

## Contexto

FASE 7 de la hoja de ruta pide un pipeline de CI con la matriz dual y gates
de merge. El backbone CUBOT.nails no trajo `.github/workflows` (nada que
deshabilitar); el deploy a Railway del backbone (`railway.json`) queda fuera
del alcance de esta fase.

## Decision

Un solo workflow `.github/workflows/pr-check.yml` con un job `build-test`
en `ubuntu-latest` (timeout 30 min, concurrency que cancela corridas previas
de la misma rama). Triggers: `pull_request` a `main` y `push` a `main` y
`fase-0/**`.

### Que corre (en orden) y que bloquea el merge

1. **gitleaks** (action oficial, historia completa): BLOQUEA si hay
   credenciales o tokens en el diff/historia. El repo es publico.
2. **dotnet build Ecorex.sln -c Release**: BLOQUEAN los errores; los
   warnings heredados del backbone (4 hoy) NO bloquean todavia.
3. **dotnet format --verify-no-changes**: NO bloquea todavia
   (`continue-on-error: true` con TODO en el YAML). Verificado en local el
   2026-07-03: el codigo actual FALLA con 33 errores WHITESPACE heredados
   en 4 archivos (LeadService, FormDefinitionService, ChatService en
   Application; Program.cs en SuperAdmin). Cuando se saneen con
   `dotnet format Ecorex.sln` en un commit propio, quitar el
   continue-on-error y el paso pasa a gate.
4. **Tests unitarios** (Domain + Application, rapidos): BLOQUEAN.
5. **Tests de integracion** (`Ecorex.Integration.Tests`): BLOQUEAN. La
   matriz dual vive en los tests, no en el YAML: cada clase corre contra
   PostgreSQL 16 Y SQL Server 2022 via fixtures de Testcontainers.
6. **dorny/test-reporter** publica el resumen de los .trx como check run
   (informativo, `fail-on-error: false`; el gate ya lo dan los `dotnet test`).

### Por que Testcontainers dentro del runner y no `services:` de Actions

- Los fixtures ya levantan `postgres:16-alpine` y `mssql/server:2022-latest`
  con la MISMA configuracion que produccion (migraciones por proveedor,
  snake_case, interceptores). Duplicar eso en `services:` seria una segunda
  fuente de verdad con puertos/credenciales propios que puede divergir.
- `services:` deja los contenedores vivos todo el job y compartidos entre
  clases; Testcontainers los crea/destruye por fixture y el aislamiento es
  identico al de la corrida local del desarrollador (misma matriz, mismos
  bugs reproducibles).
- Docker esta disponible de fabrica en `ubuntu-latest`; no se necesita
  ninguna variable `TESTCONTAINERS_*` (daemon local, Ryuk funciona sin mas).

### Medidas locales (2026-07-03, maquina de desarrollo)

restore 6 s + build Release 45 s + format 162 s + unit tests 12 s
(35 Domain + 91 Application) + integracion dual 217 s (85 tests verdes
en AMBOS motores) = ~7.5 min de punta a punta en local. En Actions sumar
descarga de imagenes (~1.6 GB el mssql) y runner mas lento: estimado
total 12-18 min, con holgura bajo el timeout de 30.

## Consecuencias

- Ningun PR a `main` mezcla sin build Release verde, formato limpio, suite
  unitaria y la matriz dual completa en verde, y sin secretos detectados.
- El deploy (blue/green) queda para una ola posterior de FASE 7; este ADR
  cubre solo integracion continua.
- Cuando se saneen los warnings heredados, subir el gate a
  `-warnaserror` en un ADR/commit propio.
