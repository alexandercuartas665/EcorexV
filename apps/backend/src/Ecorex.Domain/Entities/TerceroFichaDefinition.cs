using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de una FICHA (pildora / categoria) del Directorio General (modulo 000232),
/// CONFIGURABLE por tenant: crear, renombrar, recolorear, eliminar y reordenar. Antes las 5 fichas
/// (fiscal/comercial/cliente/proveedor/empleado) eran un catalogo HARDCODEADO en 3 sitios; ahora son
/// data-driven y esta entidad es la unica fuente de verdad. Los campos (<see cref="TerceroFieldDefinition"/>)
/// referencian su ficha por <see cref="FichaKey"/>. TENANT-SCOPED (filtro global por reflexion).
/// </summary>
public class TerceroFichaDefinition : TenantEntity
{
    /// <summary>Clave estable (slug) de la ficha, NO cambia al renombrar: "fiscal", "comercial", o el
    /// slug derivado del titulo para las que crea el tenant. Es la llave que usan los campos.</summary>
    public string FichaKey { get; set; } = null!;

    /// <summary>Nombre visible de la pildora (editable por el usuario).</summary>
    public string Title { get; set; } = null!;

    /// <summary>Descripcion/ayuda de la ficha (opcional).</summary>
    public string? Description { get; set; }

    /// <summary>Color de acento (hex, p.ej. "#1D7A4A") de la pildora. Null = color por defecto.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// Perfil de tercero que hace VISIBLE la ficha en el modal del tercero: null = siempre visible
    /// (p.ej. fiscal/comercial); "cliente" / "proveedor" / "empleado" = solo cuando el tercero tiene
    /// ese perfil. Reemplaza el mapeo fragil por indice posicional del catalogo hardcodeado.
    /// </summary>
    public string? Perfil { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Oculta la pildora en el modal del tercero (y en el compositor) sin eliminarla: sus campos
    /// se conservan y la ficha sigue siendo configurable. Distinto de eliminar.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Ficha sembrada por defecto (del prototipo). Distingue las de sistema de las que crea
    /// el tenant y permite re-sembrar sin duplicar. Las de sistema no se pueden eliminar.</summary>
    public bool IsSystem { get; set; }

    // ---- Atributos del 2do motor de contactos (Directorio Modular, Capa 8) ----
    // La ficha pasa a ser una "seccion" componible por categorias. Estos campos son NULLABLE/def para
    // no afectar al motor Clasico, que los ignora. Homologacion del sistema de gestion de columnas.

    /// <summary>Naturaleza a la que aplica la seccion: CSV de "empresa"/"contacto". Null o vacio = ambas.
    /// (Motor Modular: p.ej. "Datos laborales" solo aplica a contacto.)</summary>
    public string? AplicaA { get; set; }

    /// <summary>Areas/roles que VEN la seccion: CSV de ids de area (p.ej. "comercial,contabilidad").
    /// Null/vacio = comportamiento por defecto (admin siempre; ver reglas del motor Modular). No afecta
    /// al motor Clasico, que usa <see cref="Perfil"/>.</summary>
    public string? Areas { get; set; }

    /// <summary>Seccion protegida: no se elimina del sistema (p.ej. la "publica", identificacion minima).
    /// Si se puede activar/desactivar por categoria.</summary>
    public bool Protegida { get; set; }

    /// <summary>Icono (clase Font Awesome, p.ej. "fa-globe") para la pildora/pestana en el motor Modular.</summary>
    public string? Icono { get; set; }
}
