using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Directorio;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de DIRECTORIO: permite al agente de IA registrar un
/// contacto (tercero) en el Directorio General cuando conoce a un cliente nuevo. Reusa
/// <see cref="ITerceroService"/> (misma alta que la ficha) y el aislamiento por tenant. Es idempotente
/// por identificacion: si ya existe un tercero con ese documento, devuelve el existente en vez de
/// duplicar.
/// </summary>
public interface IDirectorioToolset : IAgentToolset { }

public sealed class DirectorioToolset : IDirectorioToolset
{
    private readonly ITerceroService _terceros;
    private readonly IApplicationDbContext _db;

    public DirectorioToolset(ITerceroService terceros, IApplicationDbContext db)
    {
        _terceros = terceros;
        _db = db;
    }

    public string GroupKey => "directorio";
    public string GroupLabel => "Directorio de contactos";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "crear_contacto",
            "Registra un CONTACTO (cliente) en el Directorio General. Usala cuando conozcas a un cliente " +
            "nuevo y quieras dejarlo guardado. Indica 'nombre' (persona o razon social) y, si los tienes, la " +
            "identificacion, ciudad, email y telefono. Si el contacto ya existe (misma identificacion), no se " +
            "duplica: se devuelve el existente.",
            """{"type":"object","properties":{"nombre":{"type":"string","description":"Nombre de la persona o razon social de la empresa"},"tipo":{"type":"string","enum":["empresa","persona"],"description":"Empresa o persona (por defecto empresa)"},"identificacion":{"type":"string","description":"Numero de documento (NIT / cedula), opcional"},"tipo_identificacion":{"type":"string","enum":["nit","cedula","correo","telefono"],"description":"Tipo de documento (opcional)"},"ciudad":{"type":"string"},"email":{"type":"string"},"telefono":{"type":"string"},"sector":{"type":"string","description":"Sector/industria (si es empresa)"},"cargo":{"type":"string","description":"Cargo (si es persona)"}},"required":["nombre"],"additionalProperties":false}"""),
    };

    public async Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch { return Err("Los argumentos no son un JSON valido."); }

        try
        {
            return toolName switch
            {
                "crear_contacto" => await CreateContactAsync(args, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<AgentToolResult> CreateContactAsync(JsonElement args, CancellationToken ct)
    {
        var nombre = Str(args, "nombre");
        if (string.IsNullOrWhiteSpace(nombre)) { return Err("Falta el nombre del contacto (nombre)."); }

        var tipo = string.Equals(Str(args, "tipo"), "persona", StringComparison.OrdinalIgnoreCase)
            ? TerceroTipo.Persona : TerceroTipo.Empresa;
        var identificacion = Str(args, "identificacion");
        var idTipo = ParseIdTipo(Str(args, "tipo_identificacion"), tipo);
        var ciudad = Str(args, "ciudad");
        var email = Str(args, "email");
        var telefono = Str(args, "telefono");
        var sector = Str(args, "sector");
        var cargo = Str(args, "cargo");

        // Idempotencia: si ya hay un tercero con esa identificacion, no duplicar (el filtro global lo acota al tenant).
        if (!string.IsNullOrWhiteSpace(identificacion))
        {
            var idv = identificacion!.Trim();
            var existente = await _db.Terceros.AsNoTracking()
                .Where(t => t.IdValor == idv)
                .Select(t => new { t.Id, t.Nombre })
                .FirstOrDefaultAsync(ct);
            if (existente is not null)
            {
                return Ok(new { ok = true, contacto_id = existente.Id, nombre = existente.Nombre, ya_existia = true,
                    mensaje = $"El contacto '{existente.Nombre}' ya estaba registrado; no se duplico." });
            }
        }

        var req = new SaveTerceroRequest(
            Nombre: nombre!.Trim(),
            Tipo: tipo,
            Perfiles: TerceroPerfil.Cliente,
            Estado: TerceroEstado.Activo,
            Ciudad: string.IsNullOrWhiteSpace(ciudad) ? null : ciudad!.Trim(),
            IdTipo: idTipo,
            IdValor: string.IsNullOrWhiteSpace(identificacion) ? null : identificacion!.Trim(),
            Sector: tipo == TerceroTipo.Empresa && !string.IsNullOrWhiteSpace(sector) ? sector!.Trim() : null,
            Cargo: tipo == TerceroTipo.Persona && !string.IsNullOrWhiteSpace(cargo) ? cargo!.Trim() : null,
            Email: string.IsNullOrWhiteSpace(email) ? null : email!.Trim(),
            Telefono: string.IsNullOrWhiteSpace(telefono) ? null : telefono!.Trim());

        var res = await _terceros.CreateAsync(req, ct);
        if (!res.IsOk || res.Value is null)
        {
            return Err(res.Error ?? "No se pudo registrar el contacto.");
        }

        return Ok(new
        {
            ok = true,
            contacto_id = res.Value.Id,
            nombre = res.Value.Nombre,
            tipo = tipo == TerceroTipo.Empresa ? "empresa" : "persona",
            mensaje = $"Contacto '{res.Value.Nombre}' registrado en el Directorio General."
        });
    }

    private static TerceroIdTipo ParseIdTipo(string? s, TerceroTipo tipo) => (s?.Trim().ToLowerInvariant()) switch
    {
        "nit" => TerceroIdTipo.Nit,
        "cedula" or "identificacion" or "cc" => TerceroIdTipo.Identificacion,
        "correo" or "email" => TerceroIdTipo.Correo,
        "telefono" => TerceroIdTipo.Telefono,
        _ => tipo == TerceroTipo.Empresa ? TerceroIdTipo.Nit : TerceroIdTipo.Identificacion
    };

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
