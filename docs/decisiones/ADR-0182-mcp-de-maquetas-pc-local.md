# ADR-0182: Servidor MCP de maquetas para el PC local (ola 2)

Fecha: 2026-11-18
Estado: Aceptada
Version: v0.16.345
Relacionado: ADR-0159 (MCP de tableros), ADR-0164 (MCP de WhatsApp), ADR-0176 (solicitudes -> tarjetas),
ADR-0179 (maquetas en la nube), ADR-0180 (editor de maquetas en el sistema)
Nota: ADR-0181 lo usa la rama feat/agentes-sistema (aun sin integrar); por eso este es el 0182.

## Contexto

Con ADR-0179/0180 las maquetas se anotan en la nube: cada ajuste es una tarjeta con captura, pantalla, archivo y
selector del elemento. El usuario pidio que el MCP "se comunique con mi pc local para los cambios en la maqueta":
la IA del PC (Claude Code abierto en la carpeta de la maqueta) debe leer esas solicitudes, aplicar los cambios en
los archivos locales y devolver el resultado a ECOREX.

## Decision

1. **Servidor MCP** `POST /api/mcp/maquetas` sobre el host JSON-RPC comun (`McpJsonRpcHost`: Streamable HTTP sin
   sesiones, auth por token personal, aislamiento por tenant con AmbientTenantContext, auditoria de escrituras en
   super_admin_audit_logs con reason `mcp-maquetas` y accion `mcp.maquetas.<herramienta>`).
2. **Token**: el MISMO token personal de alcance `integ:maqueta` (Conectar IA > "Maquetas (MCP del PC local)") abre
   este MCP, la subida de versiones (`POST /api/maqueta/proyectos/{slug}/versiones`) y el envio de solicitudes. Asi
   el PC guarda un solo secreto (variable de entorno `ECOREX_MAQUETA_TOKEN`). Un token de tableros no abre maquetas.
3. **Herramientas** (`MaquetaMcpToolset`, capa delgada):
   - Lectura: `listar_proyectos`, `listar_solicitudes` (abiertas/terminadas/todas, por archivo), `ver_solicitud`
     (contexto tecnico del metadata + detalle de la tarjeta via `ver_tarea` + la CAPTURA como bloque image),
     `listar_versiones`, `como_publicar_version` (comandos PowerShell/bash exactos; no publica).
   - Escritura (delegan en el toolset de tableros: misma visibilidad, bitacora "via IA" y reglas de flujo):
     `comentar_solicitud`, `mover_solicitud`, `resolver_solicitud` (comenta + terminar_tarea).
   - Solo operan tarjetas cuyo ExternalTaskLink tiene origen `maqueta-*` (las del editor y las del kit heredado);
     cualquier otra tarjeta responde error.
4. **Publicar** no va por MCP: un ZIP de varios MB no cabe en argumentos de herramienta (pasarian por el contexto
   del modelo). `como_publicar_version` entrega el comando que el PC corre con su token; la subida exige Owner/Admin.

## Consecuencias

- Flujo: listar_solicitudes -> ver_solicitud (ve la captura) -> editar archivos locales -> comentar_solicitud ->
  publicar (comando) -> resolver_solicitud con la version.
- La captura se devuelve hasta 4 MB (las del editor pesan ~100-500 KB).
- Pruebas: `MaquetaSolicitudesTests.McpMaquetas_*` (dual: lista, captura, comentario, resolver, aislamiento de
  tarjetas que no son de maqueta, comando de publicacion) y E2E local por HTTP con token de prueba (revocado).
