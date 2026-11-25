---
tipo: receta
tema: formularios
---

# FO07 - Receta registro transaccional con cierre por firma

Volver a [[00 - LEEME PRIMERO]].

Caso: **acta de entrega**. Cada acta tiene numero (ACT-000123), un codigo interno con las iniciales del responsable,
se guarda en borrador mientras se llena y se CIERRA sola cuando el cliente firma.

## 1. Numeracion (guia general)

```json
{"name":"set_transactional","arguments":{"form_id":"<form_id>","is_transactional":true,"identity_mode":"Sequence","identity_prefix":"ACT-","identity_padding":6}}
```
`set_transactional` solo cambia lo que envias (no apaga otras opciones).

## 2. Codigo automatico al crear

```json
{"name":"codigo_automatico_formulario","arguments":{"formulario":"ACTA-ENT","activo":true,"campo":"codigo_interno","ancho":4}}
```
Llena `codigo_interno` con iniciales del asignado + consecutivo. Conviene `campo_bloqueado` en ese campo.

## 3. Cierre automatico al firmar

```json
{"name":"cierre_automatico_formulario","arguments":{"formulario":"ACTA-ENT","campo":"firma_cliente","operador":"no_vacio"}}
```
Al guardar con la firma, el registro se ENVIA y cierra aunque se guardara como borrador. Operadores: no_vacio,
vacio, igual, distinto, contiene. Sin `campo` quita el cierre.

## 4. Otras opciones del registro

| Quiero | Herramienta |
|---|---|
| Que el registro siga editable siempre (autoguardado, nunca se cierra) | `registro_editable` activo=true (NO lo combines con cierre automatico) |
| Ocultar Enviar/Imprimir cuando se llena dentro de la creacion de una tarea | `ocultar_barra_envio` |
| Usarlo como fuente de reportes sin hacerlo modulo | `formulario_reportable` |
| Fecha/usuario de hoy por defecto | `valor_por_defecto_dinamico` (hoy, usuario_actual, usuario_asignado) |

Verifica con `get_form` y un registro de prueba (`create_record`), avisando al usuario.
