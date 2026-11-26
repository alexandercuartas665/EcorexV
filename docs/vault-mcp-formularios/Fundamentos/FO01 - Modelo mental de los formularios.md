---
tipo: fundamento
tema: formularios
---

# FO01 - Modelo mental de los formularios

Volver a [[00 - LEEME PRIMERO]].

| Pieza | Que es |
|---|---|
| **Formulario** (definicion) | La plantilla: codigo unico, titulo, estado (borrador / activo / archivado). Solo uno ACTIVO se puede llenar. |
| **Contenedor** | Seccion, segmento, pestanas o grupo. Ordena los campos en una rejilla de 12 columnas. |
| **Campo** (pregunta) | Tiene `field_code` (su nombre tecnico, el que usan reglas, calculos y reportes), etiqueta y tipo de control. |
| **Registro** (respuesta) | Cada vez que alguien lo llena. Puede quedar en borrador o enviado. Si el formulario es transaccional, tiene numero (ej. VIS-000123). |
| **Regla** | Accion del motor de reglas que corre en un momento (ver [[FO02 - Reglas que son y cuando corren]]). |
| **Plantilla de impresion** | HTML con marcadores que produce el PDF/impresion del registro (guia general R04). |

## Donde se llena un formulario

- Dentro de una **tarea** (en su pestana Formularios o en el paso de un flujo que lo pide).
- Como **modulo** del menu (bandeja de registros).
- Por **enlace publico** `/f/...` (`create_share_link`), incluso anonimo.

Las reglas AL ENVIAR corren en los tres casos. Si el registro no esta ligado a una tarea, los verbos que necesitan
tarea (completar paso, reiniciar flujo) no tienen sobre que actuar.

## Codigos de campo: la llave de todo

- Calculos: `{total}`; visibilidad: `{"field":"tipo",...}`; reglas: parametros tipo `codigo_de_campo`;
  plantillas: `{{campo.total}}`; reportes: columna `total`.
- Elige codigos cortos, en minusculas, sin tildes ni espacios (`nit_cliente`). Cambiarlos despues rompe todo lo que
  los usa.

## Comportamiento que se configura con este cerebro

| Que | Herramientas |
|---|---|
| Reglas (al enviar / campo / boton) | `regla_al_enviar`, `crear_regla` + `regla_en_campo`, `condicion_entre_campos` |
| Registro | `codigo_automatico_formulario`, `cierre_automatico_formulario`, `registro_editable`, `ocultar_barra_envio`, `formulario_reportable` |
| Campo | `campo_bloqueado`, `campo_oculto`, `valor_por_defecto_dinamico`, `permisos_campo_por_rol`, `subformulario_de_campo`, `diseno_campo`, `renombrar_campo` |
| Seguridad del trabajo | `guardar_version_formulario`, `restaurar_version_formulario`, `listar_enlaces_formulario`, `revocar_enlace_formulario` |

Editar la estructura con `update_question` o `apply_form_spec` es seguro: solo cambia lo que envias (lo demas del
campo se conserva). Para el nombre interno (`field_code`) usa `renombrar_campo`: simula primero y renombra en
cascada (ver [[FO09 - Cambios seguros y versiones]]).
