namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>
/// Catalogo compartido del mapeo "Direccionar compra" (formulario de la tarea -> conciliador DIAN) para lo que
/// NO es un campo del formulario: ORIGENES "del clic" (lo que pasa al pulsar el boton) y DESTINOS nativos extra.
/// Lo usan el verbo DIRECCIONAR_CONCILIADOR y los dos editores del mapeo (modal de la regla en el FormDesigner y
/// "Configuracion de salida" del conciliador), asi las opciones no se desincronizan. Se guardan en el mismo
/// DireccionarMapDto (FormField = "sys:...", Target = "aprobada"): sin migracion.
/// </summary>
public static class DireccionarFuentes
{
    /// <summary>Origen: valor fijo "true" (la compra quedo aprobada al pulsar el boton).</summary>
    public const string Aprobado = "sys:aprobado";

    /// <summary>Origen: usuario que pulso el boton. Va su NOMBRE; si el destino es "asignado", su id.</summary>
    public const string Usuario = "sys:usuario";

    /// <summary>Origen: fecha y hora del clic en la zona del tenant, "yyyy-MM-dd HH:mm".</summary>
    public const string FechaHora = "sys:fecha_hora";

    /// <summary>Destino nativo: el toggle APROBADA del renglon (queda como decision manual).</summary>
    public const string TargetAprobada = "aprobada";

    /// <summary>Origenes "del clic" para el desplegable de la izquierda (valor, etiqueta).</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> Origenes = new[]
    {
        (Aprobado, "Aprobado (Si)"),
        (Usuario, "Usuario que pulso el boton"),
        (FechaHora, "Fecha y hora del clic"),
    };

    public static bool EsOrigenDelClic(string? formField)
        => !string.IsNullOrWhiteSpace(formField) && formField.StartsWith("sys:", StringComparison.Ordinal);
}
