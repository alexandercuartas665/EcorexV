namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Guarda LOCALMENTE la cadena de conexion de la fuente SQL Server que el Gateway consulta (Ola C,
/// "credencial gestionada por el agente" - opcion b de doc 02/05: la credencial de la LAN NUNCA viaja
/// por el canal ni se guarda en el repo). El cifrado y la ubicacion los gobierna
/// <see cref="AgentVault"/> (ADR-0039).
/// </summary>
public sealed class GatewaySourceStore
{
    private const string FileName = "source.dat";

    /// <summary>Cadena de conexion SQL Server persistida, o null si no hay.</summary>
    public string? LoadSqlServer()
    {
        var s = AgentVault.ReadText(FileName);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public void SaveSqlServer(string connectionString) => AgentVault.WriteText(FileName, connectionString);

    public void Clear() => AgentVault.Delete(FileName);
}
