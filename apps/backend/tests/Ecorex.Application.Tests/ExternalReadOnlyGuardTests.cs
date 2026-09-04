using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.External;

namespace Ecorex.Application.Tests;

/// <summary>
/// Guarda de SOLO LECTURA del conector externo (ADR-0064). Fija que el conector rechaza cualquier cosa
/// que no sea una lectura pura: es la defensa en profundidad ADEMAS del usuario de BD de solo lectura y
/// la transaccion read-only. Pieza pura, corre sin BD.
/// </summary>
public class ExternalReadOnlyGuardTests
{
    [Theory]
    [InlineData("SELECT * FROM tareas")]
    [InlineData("  select id, nombre from encargados where id = @id ")]
    [InlineData("WITH t AS (SELECT 1 AS n) SELECT n FROM t")]
    [InlineData("SELECT * FROM tareas WHERE fecha BETWEEN @ini AND @fin;")]
    public void IsReadOnly_AllowsSingleSelectOrCte(string sql)
    {
        Assert.True(ExternalReadOnlyGuard.IsReadOnly(sql));
    }

    [Theory]
    [InlineData("INSERT INTO tareas(id) VALUES(1)")]
    [InlineData("UPDATE tareas SET nombre = 'x'")]
    [InlineData("DELETE FROM tareas")]
    [InlineData("DROP TABLE tareas")]
    [InlineData("TRUNCATE TABLE tareas")]
    [InlineData("ALTER TABLE tareas ADD col int")]
    [InlineData("SELECT * INTO copia FROM tareas")]
    [InlineData("SELECT 1; DROP TABLE tareas")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("EXEC sp_who")]
    [InlineData("SELECT * FROM t; -- ok\nDELETE FROM t")]
    [InlineData("")]
    [InlineData(null)]
    public void IsReadOnly_RejectsWritesMultiStatementAndBlank(string? sql)
    {
        Assert.False(ExternalReadOnlyGuard.IsReadOnly(sql));
    }

    [Fact]
    public void IsReadOnly_IgnoresCommentedOutVerbsButRejectsRealOnes()
    {
        // Un verbo escondido tras comentario de bloque no debe colar una escritura real.
        Assert.False(ExternalReadOnlyGuard.IsReadOnly("SELECT 1 /* */ ; DELETE FROM t"));
        // Un SELECT con la palabra 'update' solo dentro de un comentario sigue siendo lectura.
        Assert.True(ExternalReadOnlyGuard.IsReadOnly("SELECT id FROM t -- update later"));
    }

    [Fact]
    public void EnsureReadOnly_ThrowsOnWrite()
    {
        Assert.Throws<ReportValidationException>(() => ExternalReadOnlyGuard.EnsureReadOnly("DELETE FROM t"));
    }

    // ---- Opt-in AllowBatch / AllowWrite (ADR-0064): bypass controlado del guard ----

    // Batch T-SQL representativo (estilo RDL SSRS real): DECLARE + INSERT INTO @tabla + EXEC + SELECT final.
    private const string Batch =
        "DECLARE @d TABLE (id int); INSERT INTO @d(id) EXEC m700_gen.dbo.sp_visualizar_data_formulario_sql @p; SELECT * FROM @d";

    [Fact]
    public void EnsureReadOnly_WithoutOptIn_RejectsBatch()
    {
        // Sin opt-in (default) el guard sigue estricto: el batch se rechaza como hasta ahora.
        Assert.Throws<ReportValidationException>(
            () => ExternalReadOnlyGuard.EnsureReadOnly(Batch, allowWrite: false, allowBatch: false));
    }

    [Fact]
    public void EnsureReadOnly_AllowBatch_SkipsGuard()
    {
        // Opt-in por dataset: el batch pasa sin lanzar (lo autoriza el dueño del tenant).
        ExternalReadOnlyGuard.EnsureReadOnly(Batch, allowWrite: false, allowBatch: true);
    }

    [Fact]
    public void EnsureReadOnly_AllowWrite_SkipsGuard()
    {
        // Conexion con escritura habilitada: tambien omite el guard (comportamiento previo, ahora centralizado).
        ExternalReadOnlyGuard.EnsureReadOnly(Batch, allowWrite: true, allowBatch: false);
    }

    [Fact]
    public void EnsureReadOnly_OverloadWithoutOptIn_StillAllowsPlainSelect()
    {
        // El overload no cambia el caso normal: una lectura pura sigue pasando sin opt-in.
        ExternalReadOnlyGuard.EnsureReadOnly("SELECT id FROM t WHERE id = @id", allowWrite: false, allowBatch: false);
    }
}
