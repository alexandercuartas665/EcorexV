# ADR-0156: Saneo de surrogates sueltos en el punto de escritura (anti EncoderFallbackException)

**Status:** Accepted
**Date:** 2026-11-08
**Deciders:** Alexander Cuartas (dev principal)

## Contexto

En produccion se repetian fallos de `SaveChanges` en la ruta del agente de IA
(`AgentReplyDispatcher` -> `AgentConversationService`): ~89 en una ventana de ~15 h
(2026-11-07/08), todos con la misma causa raiz:

```
Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes.
 ---> System.Text.EncoderFallbackException: Unable to translate Unicode character \uD83D at index 125 to specified code page.
```

`\uD83D` es la mitad ALTA de un par suplente UTF-16 de emoji (p.ej. un rostro sonriente = `D83D DE00`).
Un "surrogate suelto" (media mitad sin su pareja) es UTF-16 invalido y **no se puede
codificar a UTF-8**, asi que Npgsql revienta al escribir el parametro. PostgreSQL ni
siquiera puede almacenar texto asi, por lo que el dato no venia de la BD: se generaba en
memoria durante la corrida del agente (por un truncado que partia el emoji, o texto mal
decodificado de una fuente externa: webhook de WhatsApp/Evolution, respuesta del proveedor
de IA, pegado del usuario).

Agravante: en la ruta del agente el texto se **envia al cliente antes** de persistirse.
Como el guardado fallaba despues, el reintento acotado (3x) + el barrido de respaldo del
despachador **reprocesaban la misma conversacion**, reenviando el mensaje al cliente y
dejando ~89 fallos encadenados por un unico mensaje problematico.

Hallado al auditar el bug de concurrencia de DbContext (ver
[[dbcontext-efimero-lectura-factory]]); es un bug DISTINTO y no relacionado.

## Decision

Sanear los surrogates sueltos en el **punto unico de escritura**, con un
`SaveChangesInterceptor` registrado en **ambos** proveedores (DAL dual, ADR-001):

- `SurrogateText` (en `Ecorex.Application/Common`): helper reusable y testeable.
  - `StripLoneSurrogates(string?)`: reemplaza cada mitad suelta por `U+FFFD`; conserva los
    pares bien formados. Camino rapido: si no hay ningun suelto devuelve la MISMA referencia
    (sin asignar), para que el interceptor no reescriba los mensajes con emojis correctos.
  - `SafeTruncate(string?, int)`: trunca sin partir un par suplente (si el corte caeria entre
    las dos mitades, retrocede un caracter).
  - `HasLoneSurrogate(string?)`: deteccion para el camino rapido.
- `SurrogateSanitizingInterceptor` (en `Ecorex.Infrastructure/Persistence/Interceptors`):
  antes de cada `SaveChanges`, recorre las entidades Added/Modified y sanea sus propiedades
  string (en Modified, solo las que de verdad cambian, para no ampliar el UPDATE). Corre en
  el mismo `SaveChanges` -> atomico. Registrado singleton (sin estado) y agregado via
  `AddInterceptors` en las ramas Postgres y SQL Server de `AddInfrastructure`.

## Opciones consideradas

### Opcion A: arreglar el truncado concreto que partia el emoji
No se encontro un unico `Substring(125)` localizable; el surrogate suelto puede entrar por
varias fuentes (truncado, decodificado del proveedor, texto externo). Arreglar un solo punto
dejaria vivos los demas origenes.

### Opcion B: sanear el texto de la respuesta del agente en `AiInferenceService`
Cubriria la ruta del agente, pero no el resto (formularios, scraping, pegados), y ese servicio
transforma el texto (marcadores de cierre, adjuntos); tocarlo arriesgaba la logica reciente de
SARA (ver [[sara-rol-fijo-cierres-extraprompt]]).

### Opcion C (elegida): guardia en el interceptor de SaveChanges
| Dimension | Evaluacion |
|-----------|------------|
| Cobertura | Alta (todo origen y toda entidad, en un solo lugar) |
| DAL dual | Nativa (se aplica igual a PG y SQL Server) |
| Atomicidad | Intacta (corre dentro del mismo SaveChanges) |
| Costo | Bajo (camino rapido sin asignacion cuando no hay sueltos) |

## Consecuencias

- Una escritura con un surrogate suelto ya no revienta: el caracter invalido queda como `U+FFFD`.
  Se corta el bucle de reintentos y, con el, los reenvios duplicados al cliente del agente.
- `SafeTruncate` queda disponible para que los truncados que alimentan columnas no partan emojis.
- El interceptor corre en CADA SaveChanges del sistema; el camino rapido (sin sueltos) es una
  pasada lineal sin reasignar, asi que el costo es despreciable salvo lotes muy grandes.

## Verificacion

- `dotnet build Ecorex.sln`: verde (0 errores).
- Unitarios del helper (`SurrogateTextTests`, Application.Tests): 16/16. Suite completa 1195/1195.
- Interceptor (`SurrogateSanitizingInterceptorTests`, Integration.Tests, provider en memoria, sin
  Docker): 3/3 (saneo en Added/Modified, emoji valido intacto, texto limpio sin tocar).
- Matriz dual de integracion (PG + SQL Server, Testcontainers) + aislamiento cross-tenant: gate del
  CI (ADR-0018) en el PR a main.

## Action Items

1. [x] Helper `SurrogateText` (StripLoneSurrogates / SafeTruncate / HasLoneSurrogate) + tests.
2. [x] `SurrogateSanitizingInterceptor` registrado en ambos proveedores (DAL dual) + test.
3. [x] Deploy a prod: DESPLEGADO v0.16.280 (2026-11-08, via rapida GHCR; prod sirve v0.16.280, HTTP 200).
4. [x] Aplicar `SafeTruncate` a los truncados que alimentan columnas persistidas (v0.16.281, 2026-11-08:
   23 reemplazos en 16 servicios; SIN deploy). La deuda estructural de concurrencia que se detecto en la
   misma auditoria se traslada a su propio ADR (ver ADR-0157).
