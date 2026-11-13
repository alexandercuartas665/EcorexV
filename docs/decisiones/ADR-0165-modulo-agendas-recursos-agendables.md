# ADR-0165: Modulo de Agendas generico (recursos agendables, agendas base y agenda del recurso)

**Estado:** Aceptado (olas 1 y 2 implementadas en v0.16.313; olas 3 a 6 propuestas)
**Fecha:** 2026-11-12
**Relacionado:** proyecto hermano Visal (modulos "Agendas base", "Agendas de profesionales", "Calendario",
"Novedades de profesionales"); ADR-0106 (calendario operativo del tenant); CLAUDE.md seccion 7 (se elimino del
backbone la agenda del dominio belleza).

## Contexto

Muchos clientes de ECOREX gestionan agendas: necesitan separar turnos de atencion y que un agente los programe.
El proyecto hermano Visal ya resolvio el caso para medicos con un proceso en dos pasos: una agenda BASE
(plantilla semanal independiente) que se IMPORTA (copia) a cada profesional, mas novedades (vacaciones,
incapacidad) y un calendario de festivos.

ECOREX no es solo para medicos: la agenda puede ser de una persona, una sala, una maquina, un vehiculo o una
linea de atencion. Y el backbone del que se clono ECOREX traia una agenda del dominio belleza (citas de salon,
asesores, no-show) que se ELIMINO a proposito (CLAUDE.md seccion 7). Hoy no queda codigo de ese dominio; la unica
entidad "Cita" del repo es la agenda del Gestor de Clientes (000740), una funcion viva del CRM que no se toca.

## Decision

1. **Modulo nuevo y generico**, construido desde cero copiando el PROCESO de Visal (no su codigo): agenda base ->
   importar al recurso -> editar la copia -> novedades -> disponibilidad.
2. **"Profesional" pasa a "recurso agendable"**, con TIPOS definidos por cada tenant (`AgendaTipoRecurso`: nombre,
   icono, color y a que se vincula). El sistema sugiere Persona, Grupo, Espacio, Equipo, Vehiculo y Punto de
   atencion. Un tipo "Persona" se vincula a un usuario del tenant; el resto son recursos libres.
3. **Cupo en paralelo por franja**: cada franja tiene dia, inicio, fin, bloque (minutos) y cupo (turnos simultaneos
   por bloque). Cupos de la franja = bloques completos x cupo. Sirve para salas con aforo o lineas con varios
   asesores.
4. **Vigencias**: la agenda propia del recurso es una lista de `AgendaRecursoVigencia` (desde/hasta). Para una fecha
   manda la de inicio mas reciente que la cubre, asi un "horario de temporada" se monta encima del normal sin
   borrarlo. La plantilla origen queda solo como traza: borrarla no toca las copias.
5. **Disponibilidad** = franjas de la vigencia que aplica - dia cerrado por el CALENDARIO OPERATIVO del tenant
   (reutiliza `TenantOperatingDays` y su precarga de festivos de Colombia, ADR-0106) - bloques tocados por las
   novedades del recurso (dia completo o tramo horario). Calculo puro en `AgendaCalculo` (testeable sin BD).
6. **Menu y permisos**: seccion "Agendas" con "Recursos agendables" (`agendas/recursos`, 001683) y "Agendas base"
   (`agendas/base`, 001684); el Route es el ModuleKey de la matriz de roles. Se siembra con
   `ECOREX_MENU_AGENDAS=true` (Standard + Demo) o `ECOREX_MENU_AGENDAS_TENANT=<nombre>` (un solo tenant).
7. Reglas del proyecto: tenant-scoped por filtro global, borrado logico de los agregados (tipo, recurso, plantilla,
   vigencia, novedad), franjas reemplazadas en bloque dentro de una transaccion, concurrencia optimista
   (`Version`) en recurso y plantilla, fechas en la zona horaria del tenant. Migracion aditiva dual `AddAgendas`.

## Opciones consideradas

| Opcion | Pro | Contra |
|---|---|---|
| A. Copiar el modulo de Visal tal cual (profesional) | Rapido | Solo sirve a personas; no cubre salas, maquinas ni lineas |
| B. Reusar la agenda del Gestor de Clientes (Cita) | Ya existe | Es una agenda de eventos del CRM, sin plantillas ni cupos |
| **C. Modulo generico de recursos (elegida)** | Cubre cualquier entidad; el proceso de Visal se conserva | Mas piezas que configurar (tipos de recurso) |

## Olas

1. (hecha) Tipos de recurso, recursos agendables, agendas base.
2. (hecha) Agenda del recurso (importar, vigencias, editar), novedades, disponibilidad semanal con el calendario.
3. (propuesta) Servicios agendables: duracion, recursos requeridos a la vez (p.ej. tecnico + vehiculo), colchones,
   anticipacion minima/maxima, concepto de tarea que nace al reservar.
4. (propuesta) Turnos como TAREAS de ECOREX (TaskItem con fecha y recursos): vistas Tablero/Lista/Calendario y
   concurrencia optimista para no entregar el mismo cupo dos veces.
5. (propuesta) Herramientas del agente: consultar_disponibilidad, reservar_turno, turnos_del_cliente, reprogramar,
   cancelar; recordatorio y "no asistio". `AgendaRecurso.AgenteHabilitado` ya existe para gobernarlo.
6. (propuesta) Reserva publica por enlace.

## Consecuencias

- Cada tenant puede montar agendas de cualquier cosa sin codigo, con el mismo proceso que ya usa Visal.
- La disponibilidad aun no descuenta turnos tomados (llega con la ola 4).
- Pendiente para olas siguientes: grupos con miembros (asignar al libre), vinculo a contactos del Directorio,
  calendario por sede (hoy el calendario es del tenant).

## Verificacion

- `AgendaCalculoTests` (unitarias): cupos, validacion de franjas, vigencia que aplica, novedades parciales, festivo.
- `AgendasTests` matriz dual PG + SQL Server 8/8: importar como copia independiente, semana con festivo y novedad,
  validaciones, concurrencia optimista, duplicar, tipo en uso, AISLAMIENTO cross-tenant.
- E2E en dev (5234, AGROMETALICAS): tipos sugeridos -> agenda base -> recurso Persona -> importar -> novedad parcial
  -> disponibilidad (viernes 8 de 16 cupos por la capacitacion de la tarde).
