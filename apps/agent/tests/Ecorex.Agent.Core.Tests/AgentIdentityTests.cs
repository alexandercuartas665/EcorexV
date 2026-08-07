using Ecorex.Agent.Core.Services;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Tests;

/// <summary>
/// Reglas de re-fijar la identidad (ADR-0063 / ADR-0050): el criterio del secreto es el que permite
/// reconfigurar el agente por MSI/UAC sin exponer la credencial. Puro, sin DPAPI.
/// </summary>
public class AgentIdentityTests
{
    private static readonly AgentConfig Existing = new("cli_viejo", "https://viejo.example.com/hubs/agente", "secreto-valido");

    [Fact]
    public void Merge_secreto_vacio_conserva_el_actual()
    {
        var result = AgentIdentity.Merge("cli_nuevo", "https://nuevo.example.com/hubs/agente", "", Existing);

        Assert.Equal("cli_nuevo", result.ClientId);
        Assert.Equal("https://nuevo.example.com/hubs/agente", result.HubUrl);
        Assert.Equal("secreto-valido", result.Secret); // conservado
        Assert.True(result.HasSecret);
    }

    [Fact]
    public void Merge_secreto_null_conserva_el_actual()
    {
        var result = AgentIdentity.Merge("cli_nuevo", "https://nuevo.example.com/hubs/agente", null, Existing);

        Assert.Equal("secreto-valido", result.Secret);
    }

    [Fact]
    public void Merge_secreto_solo_espacios_conserva_el_actual()
    {
        var result = AgentIdentity.Merge("cli_nuevo", "https://nuevo.example.com/hubs/agente", "   ", Existing);

        Assert.Equal("secreto-valido", result.Secret);
    }

    [Fact]
    public void Merge_centinela_KEEP_conserva_el_actual()
    {
        var result = AgentIdentity.Merge("cli_nuevo", "https://nuevo.example.com/hubs/agente",
            AgentIdentity.KeepSecretSentinel, Existing);

        Assert.Equal("secreto-valido", result.Secret); // el centinela nunca se escribe como secreto
    }

    [Fact]
    public void Merge_secreto_nuevo_rota_la_credencial()
    {
        var result = AgentIdentity.Merge("cli_nuevo", "https://nuevo.example.com/hubs/agente", "secreto-rotado", Existing);

        Assert.Equal("secreto-rotado", result.Secret);
    }

    [Fact]
    public void Merge_recorta_espacios_de_clientId_hub_y_secreto()
    {
        var result = AgentIdentity.Merge("  cli_nuevo  ", "  https://x.example.com  ", "  secreto-rotado  ", Existing);

        Assert.Equal("cli_nuevo", result.ClientId);
        Assert.Equal("https://x.example.com", result.HubUrl);
        Assert.Equal("secreto-rotado", result.Secret);
    }

    [Fact]
    public void Merge_sobre_vault_vacio_sin_secreto_queda_sin_secreto()
    {
        var result = AgentIdentity.Merge("cli_nuevo", "https://x.example.com", "", AgentConfig.Empty);

        Assert.Equal(string.Empty, result.Secret);
        Assert.False(result.HasSecret);
    }
}
