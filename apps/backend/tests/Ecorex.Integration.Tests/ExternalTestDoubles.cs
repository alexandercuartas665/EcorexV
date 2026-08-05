using Ecorex.Application.Common;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.External;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Dobles ligeros para construir el <see cref="ExternalReportReader"/> en tests del motor de reportes que
/// NO ejercitan datos externos (solo necesitan satisfacer el nuevo parametro del catalogo/datasource,
/// ADR-0063). El protector es passthrough y el executor lanza si alguien intenta ejecutar (nunca deberia
/// en estos tests, ya que ningun tenant tiene fuentes externas concedidas).
/// </summary>
internal static class ExternalTestDoubles
{
    public static ExternalReportReader Reader(EcorexDbContext ctx) =>
        new(ctx, new PassthroughProtector(), new UnusedExecutor());

    private sealed class PassthroughProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class UnusedExecutor : IExternalQueryExecutor
    {
        public Task<ReportDataSet> ExecuteAsync(ExternalQuery query, CancellationToken ct = default) =>
            throw new InvalidOperationException("No debe ejecutarse una consulta externa en este test.");

        public Task<string?> TestConnectionAsync(ExternalDataProvider provider, string connectionString, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
