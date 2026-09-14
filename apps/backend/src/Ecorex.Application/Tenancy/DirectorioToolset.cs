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

        // Telefono REAL de la conversacion (ADR-0101 rev.2, mismo criterio que crear_tarea): gana el
        // ContactPhone de la conversacion en curso; el arg 'telefono' del modelo solo es respaldo cuando NO
        // hay conversacion (el modelo suele ALUCINAR el numero). Solo aplica con AiToolRunContext (agente).
        if (AiToolRunContext.ConversationId is Guid convPhoneId)
        {
            var convPhone = await _db.Conversations.AsNoTracking()
                .Where(c => c.Id == convPhoneId).Select(c => c.ContactPhone).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(convPhone)) { telefono = convPhone; }
        }
        var telefonoDigitos = Digits(telefono);   // solo digitos: es lo que se guarda como telefono del contacto
        var telefono10 = Last10(telefono);         // ultimos 10 digitos: llave de deduplicacion por telefono

        // Idempotencia (a) por identificacion: si ya hay un tercero con ese documento, no duplicar (el filtro
        // global lo acota al tenant). Se excluyen los inactivos (baja/soft-delete).
        if (!string.IsNullOrWhiteSpace(identificacion))
        {
            var idv = identificacion!.Trim();
            var existente = await _db.Terceros.AsNoTracking()
                .Where(t => t.IdValor == idv && t.Estado != TerceroEstado.Inactivo)
                .Select(t => new { t.Id, t.Nombre })
                .FirstOrDefaultAsync(ct);
            if (existente is not null)
            {
                return Ok(new { ok = true, contacto_id = existente.Id, nombre = existente.Nombre, ya_existia = true,
                    mensaje = $"El contacto '{existente.Nombre}' ya estaba registrado; no se duplico." });
            }
        }
        // Idempotencia (b) por TELEFONO cuando NO viene identificacion: un cliente que vuelve por el MISMO
        // numero no debe crear un contacto duplicado. Se compara SOLO por digitos y por los ULTIMOS 10 (asi
        // "573001234567" == "3001234567", absorbiendo el prefijo de pais). Excluye inactivos.
        else if (telefono10 is not null)
        {
            // Pre-filtro en SQL por el sufijo de digitos (barato) y verificacion final por ultimos-10 en memoria.
            var candidatos = await _db.Terceros.AsNoTracking()
                .Where(t => t.Estado != TerceroEstado.Inactivo && t.Telefono != null
                    && EF.Functions.Like(t.Telefono, "%" + telefono10))
                .Select(t => new { t.Id, t.Nombre, t.Telefono })
                .ToListAsync(ct);
            var porTelefono = candidatos.FirstOrDefault(c => Last10(c.Telefono) == telefono10);
            if (porTelefono is not null)
            {
                return Ok(new { ok = true, contacto_id = porTelefono.Id, nombre = porTelefono.Nombre, ya_existia = true,
                    mensaje = $"El contacto '{porTelefono.Nombre}' ya estaba registrado con ese telefono; no se duplico." });
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
            Telefono: telefonoDigitos);

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

    /// <summary>Deja SOLO los digitos de un telefono (quita +, espacios, guiones, parentesis). Null si no queda ninguno.</summary>
    private static string? Digits(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var d = new string(s.Where(char.IsDigit).ToArray());
        return d.Length == 0 ? null : d;
    }

    /// <summary>Ultimos 10 digitos de un telefono (para deduplicar absorbiendo el prefijo de pais). Null si no hay digitos.</summary>
    private static string? Last10(string? s)
    {
        var d = Digits(s);
        return d is null ? null : (d.Length > 10 ? d[^10..] : d);
    }

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
