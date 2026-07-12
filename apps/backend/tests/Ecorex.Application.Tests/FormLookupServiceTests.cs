using Ecorex.Application.Forms.Lookups;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Unit tests de la fachada de lookups (ola F1, doc 01 D4): resuelve el adaptador correcto por
/// <see cref="FormSourceKind"/> y delega; un origen sin adaptador es un error tipado (no un
/// silencioso null). El aislamiento cross-tenant real (filtro global de EF) se prueba en
/// Ecorex.Integration.Tests contra Postgres Y SQL Server (Testcontainers), no aqui.
/// </summary>
public class FormLookupServiceTests
{
    private sealed class FakeSource(FormSourceKind kind) : IFormLookupSource
    {
        public int Searches { get; private set; }
        public FormSourceKind Kind => kind;

        public Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default)
        {
            Searches++;
            var item = new FormLookupItem($"{kind}-1", $"{kind} uno",
                new Dictionary<string, string?> { ["k"] = "v" });
            return Task.FromResult(new FormLookupPage([item], 1, false));
        }

        public Task<FormLookupItem?> ResolveAsync(string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
            => Task.FromResult<FormLookupItem?>(new FormLookupItem(value, $"{kind} {value}", new Dictionary<string, string?>()));

        public Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FormLookupSourceOption>>([new FormLookupSourceOption("", kind.ToString())]);

        public Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(string? sourceRef, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FormLookupFieldMeta>>([new FormLookupFieldMeta("id", "Id")]);
    }

    private static FormLookupRequest Req(FormSourceKind kind)
        => new(kind, null, "x", null, "nombre", ["id"]);

    [Fact]
    public async Task SearchAsync_despacha_al_adaptador_del_origen()
    {
        var tercero = new FakeSource(FormSourceKind.Tercero);
        var item = new FakeSource(FormSourceKind.Item);
        var svc = new FormLookupService([tercero, item]);

        var page = await svc.SearchAsync(Req(FormSourceKind.Tercero));

        Assert.Equal(1, tercero.Searches);
        Assert.Equal(0, item.Searches);
        Assert.Single(page.Items);
        Assert.Equal("Tercero-1", page.Items[0].Value);
    }

    [Fact]
    public async Task SearchAsync_cada_origen_usa_su_propio_adaptador()
    {
        var tercero = new FakeSource(FormSourceKind.Tercero);
        var item = new FakeSource(FormSourceKind.Item);
        var container = new FakeSource(FormSourceKind.DataContainer);
        var svc = new FormLookupService([tercero, item, container]);

        await svc.SearchAsync(Req(FormSourceKind.Item));
        await svc.SearchAsync(Req(FormSourceKind.DataContainer));

        Assert.Equal(0, tercero.Searches);
        Assert.Equal(1, item.Searches);
        Assert.Equal(1, container.Searches);
    }

    [Fact]
    public async Task Origen_sin_adaptador_lanza_error_tipado()
    {
        // Solo Tercero registrado: pedir Item debe fallar explicito (no null silencioso).
        var svc = new FormLookupService([new FakeSource(FormSourceKind.Tercero)]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SearchAsync(Req(FormSourceKind.Item)));
    }

    [Fact]
    public async Task ResolveAsync_valida_via_el_adaptador_del_origen()
    {
        var svc = new FormLookupService([new FakeSource(FormSourceKind.Tercero)]);

        var resolved = await svc.ResolveAsync(FormSourceKind.Tercero, "", "abc", ["id"]);

        Assert.NotNull(resolved);
        Assert.Equal("abc", resolved!.Value);
    }
}
