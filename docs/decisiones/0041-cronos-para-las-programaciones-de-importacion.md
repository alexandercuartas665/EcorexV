# ADR-0041: Cronos para el cron de las programaciones de importacion, y por que NO se reusa el motor de recurrencia existente

- Estado: aceptada
- Fecha: 2026-07-17
- Relacionada con: ADR-0040 (la credencial viaja), doc 05 Ola 4 (scheduler del agente),
  modulo 000889 (Programar actividad), modulo Contenedor de datos.

## Contexto

`ImportProcess` (la "programacion" de un contenedor) nacio con
`ScheduleKind { Manual, Interval, Cron }`, `IntervalMinutes` y `CronExpression`, pero **sin ejecutor**:
los campos se guardaban y nadie los miraba. Al conectar el disparo automatico habia que decidir de
donde sale "la proxima ejecucion".

### Por que no se reusa el motor de 000889

El modulo Programar actividad ya tiene un motor de recurrencia propio y probado
(`ScheduledJobRecurrence.ComputeNextRun`), asi que lo primero fue intentar reusarlo. **No encaja**:

- Recibe un `ScheduledJobRule` **concreto** (no una interfaz ni un DTO), asi que un `ImportProcess`
  no puede pasar por el sin torcer uno de los dos modelos.
- Su modelo es de **frecuencias de calendario** (`Once/Daily/Weekly/Monthly` + horas de disparo
  intradia). No contempla ni "cada N minutos" ni expresiones cron, que es justo lo que
  `ImportProcess` declara.

Forzar el reuso significaba meter `Interval`/`Cron` dentro de un enum de frecuencias que no los
trata, o generalizar `ComputeNextRun` a una abstraccion comun. Lo segundo es tentador, pero hoy los
dos modelos no comparten forma: uno programa **actividades para personas** (dias de la semana, hora
laboral) y el otro programa **refrescos de datos** (cada 15 minutos, o un cron). Se prefiere la
duplicacion honesta de ~60 lineas a una abstraccion prematura que ate dos dominios distintos.

Del modulo 000889 SI se reusa lo que de verdad es comun, y es lo mas valioso:

- `ScheduledJobRecurrence.ResolveTimeZone` (zona del tenant, default `America/Bogota`);
- **el patron de la bitacora**: entidad de corridas + **indice unico de idempotencia** que hace que un
  doble disparo choque en `SaveChanges` en vez de duplicar trabajo;
- **el patron del worker**: `PeriodicTimer`, barrido cross-tenant que devuelve solo `TenantId`s,
  luego scope propio + `AmbientTenantContext.Begin(tenantId)`, con techo por pasada.

## Decisiones

### 1. Se agrega el paquete **Cronos** (MIT) para las expresiones cron

No habia NINGUNA libreria de cron en el repo: el desplegable ofrecia "Cron" y detras no habia motor.
Escribir un parser de cron a mano es un clasico error de calculo: el formato tiene casos crueles
(rangos con paso, `L`, `#`, y sobre todo **DST**: horas que no existen y horas que ocurren dos veces).
Cronos es el estandar de facto en .NET, no arrastra dependencias transitivas y resuelve DST de forma
explicita (`GetNextOccurrence` con `TimeZoneInfo`).

### 2. `Interval` se calcula a mano; NO necesita libreria

"Cada N minutos" es `ultima + N`, anclado al reloj y no a la duracion de la corrida anterior. Meter
eso en una expresion cron seria retorcerlo.

### 3. Una programacion que no puede calcular su proxima ejecucion se DESACTIVA con motivo

Si un cron es invalido, la alternativa silenciosa (no disparar nunca) es la peor: el operador cree
que esta programado. Se registra en la bitacora y la programacion queda inactiva con el motivo a la
vista.

## Consecuencias

- Dependencia nueva en un repo publico: **Cronos** (MIT, sin transitivas). Se asume a cambio de no
  mantener un parser de cron propio.
- Quedan DOS motores de recurrencia en la solucion (000889 y contenedores). Es deliberado y esta
  documentado aqui: si algun dia los dos modelos convergen (por ejemplo, si las importaciones
  necesitan "cada lunes a las 8"), este ADR es el sitio donde reabrir la unificacion.
