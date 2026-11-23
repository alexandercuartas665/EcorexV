using System.IO.Compression;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Formacion;
using Ecorex.Application.Storage;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// ADR-0202 (modulo de Formacion, ola 1) en matriz dual PostgreSQL / SQL Server:
/// - Estructura del curso: modulos y lecciones con orden, validacion de enlaces YouTube/Vimeo, texto saneado, subida
///   por tipo (video/PDF/wizard .html envuelto en ZIP) y cambio de tipo que libera el archivo anterior.
/// - Catalogo: solo publicados; obligatorio por asignacion a todos, a un usuario o a una unidad del organigrama
///   (incluye descendientes) con la fecha limite mas cercana y "vencido"; borrador solo con permiso de editar.
/// - Avance persistido por leccion: porcentaje, completado y recalculo al agregar lecciones.
/// - Aislamiento: otro tenant no ve cursos ni recursos; el wizard firmado no cruza tenants; borrado logico.
/// </summary>
public abstract class FormacionModuleTestsBase
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 22, 15, 0, 0, TimeSpan.Zero); // 10:00 en Bogota

    private readonly TenantIsolationDbFixture _fixture;

    protected FormacionModuleTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Admin_EstructuraOrdenTiposYArchivos()
    {
        var t = await SeedTenantAsync("Formacion Admin");
        var admin = await SeedUserAsync(t, "admin");
        var files = new MemFiles();
        await using var ctx = _fixture.CreateContext(t);
        var svc = Svc(ctx, t, admin.PlatformUserId, files);

        var c = await svc.GuardarCursoAsync(new GuardarCursoRequest(null, "  Induccion SST  ", "Seguridad basica", "Seguridad", false, 45));
        Assert.True(c.Ok, c.Error);
        var cursoId = c.Id!.Value;
        Assert.False((await svc.GuardarCursoAsync(new GuardarCursoRequest(null, "   ", null, null, false, null))).Ok);

        var m1 = (await svc.GuardarModuloAsync(new GuardarModuloRequest(cursoId, null, "1. Introduccion", null))).Id!.Value;
        var m2 = (await svc.GuardarModuloAsync(new GuardarModuloRequest(cursoId, null, "2. Riesgos", null))).Id!.Value;
        var lVideo = (await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m1, null, "Bienvenida", null, CursoLeccionTipo.Video, null, null, 5))).Id!.Value;
        var lPdf = (await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m1, null, "Manual", null, CursoLeccionTipo.Pdf, null, null, null))).Id!.Value;

        // Enlace: solo YouTube/Vimeo.
        var malo = await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m2, null, "Video externo", null, CursoLeccionTipo.VideoEnlace, "https://example.com/v.mp4", null, null));
        Assert.False(malo.Ok);
        var lYt = (await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m2, null, "Charla", null, CursoLeccionTipo.VideoEnlace,
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10", null, null))).Id!.Value;
        // Texto: se sanea (sin scripts ni manejadores).
        var lTxt = (await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m2, null, "Lectura", null, CursoLeccionTipo.Texto, null,
            "<h2>Normas</h2><p onclick=\"x()\">Usa EPP</p><script>alert(1)</script>", null))).Id!.Value;

        // Subidas por tipo.
        Assert.True((await svc.SubirArchivoLeccionAsync(lVideo, Bytes(2048), "bienvenida.mp4", "video/mp4")).Ok);
        Assert.False((await svc.SubirArchivoLeccionAsync(lPdf, Bytes(10), "manual.docx", null)).Ok);
        Assert.True((await svc.SubirArchivoLeccionAsync(lPdf, Bytes(100), "manual.pdf", "application/pdf")).Ok);
        var lWiz = (await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m2, null, "Wizard", null, CursoLeccionTipo.WizardHtml, null, null, null))).Id!.Value;
        Assert.False((await svc.SubirArchivoLeccionAsync(lWiz, Zip(("readme.txt", "hola")), "malo.zip", "application/zip")).Ok);
        Assert.True((await svc.SubirArchivoLeccionAsync(lWiz, new MemoryStream(Encoding.UTF8.GetBytes("<html><body>Paso 1</body></html>")), "wizard.html", "text/html")).Ok);

        // Orden: la leccion PDF sube al primer lugar del modulo 1; el modulo 2 sube al primero.
        Assert.True((await svc.MoverLeccionAsync(lPdf, -1)).Ok);
        Assert.True((await svc.MoverModuloAsync(m2, -1)).Ok);

        var d = (await svc.DetalleAsync(cursoId))!;
        Assert.Equal("Induccion SST", d.Titulo);
        Assert.Equal(new[] { m2, m1 }, d.Modulos.Select(m => m.Id).ToArray());
        Assert.Equal(new[] { lPdf, lVideo }, d.Modulos[1].Lecciones.Select(l => l.Id).ToArray());
        var yt = d.Modulos[0].Lecciones.Single(l => l.Id == lYt);
        Assert.Equal("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", yt.EmbedUrl);
        var txt = d.Modulos[0].Lecciones.Single(l => l.Id == lTxt);
        Assert.Contains("Normas", txt.Contenido);
        Assert.DoesNotContain("<script", txt.Contenido, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", txt.Contenido, StringComparison.OrdinalIgnoreCase);
        var video = d.Modulos[1].Lecciones.Single(l => l.Id == lVideo);
        Assert.Equal(2048, video.ArchivoBytes);
        Assert.Equal($"/formacion/recurso/{lVideo}", video.RecursoUrl);
        var wiz = d.Modulos[0].Lecciones.Single(l => l.Id == lWiz);
        Assert.Equal("index.html", wiz.WizardEntryPath);
        Assert.True(wiz.TieneContenido);
        Assert.Equal(3, files.Count); // video + PDF + ZIP del wizard; las subidas rechazadas no dejan huerfanos

        // Cambiar el tipo libera el archivo anterior (un video no sirve como imagen).
        var archivoVideo = video.ArchivoId!.Value;
        Assert.True((await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m1, lVideo, "Bienvenida", null, CursoLeccionTipo.Imagen, null, null, null))).Ok);
        Assert.False(files.Exists(archivoVideo));
        var d2 = (await svc.DetalleAsync(cursoId))!;
        Assert.Null(d2.Modulos[1].Lecciones.Single(l => l.Id == lVideo).ArchivoId);

        // Eliminar un modulo borra sus lecciones y libera sus archivos.
        Assert.True((await svc.EliminarModuloAsync(m1)).Ok);
        var d3 = (await svc.DetalleAsync(cursoId))!;
        Assert.Single(d3.Modulos);
        Assert.Equal(1, files.Count); // solo queda el ZIP del wizard
    }

    [Fact]
    public async Task Catalogo_AsignacionesPorUnidadUsuarioYTodos_AvancePersistido()
    {
        var t = await SeedTenantAsync("Formacion Catalogo");
        var admin = await SeedUserAsync(t, "admin");
        var ana = await SeedUserAsync(t, "ana");
        var beto = await SeedUserAsync(t, "beto");
        Guid dependencia, cargo;
        await using (var seed = _fixture.CreateContext(t))
        {
            var dep = new OrgUnit { TenantId = t, Name = "Operaciones", Classifier = OrgUnitClassifier.Dependencia };
            var car = new OrgUnit { TenantId = t, Name = "Soldador", Classifier = OrgUnitClassifier.Cargo, ParentId = dep.Id };
            seed.OrgUnits.AddRange(dep, car);
            seed.OrgUnitMembers.Add(new OrgUnitMember { TenantId = t, OrgUnitId = car.Id, TenantUserId = ana.TenantUserId });
            await seed.SaveChangesAsync();
            dependencia = dep.Id;
            cargo = car.Id;
        }

        Guid cursoA, cursoB, cursoBorrador, l1, l2;
        await using (var ctx = _fixture.CreateContext(t))
        {
            var svc = Svc(ctx, t, admin.PlatformUserId, new MemFiles());
            cursoA = (await svc.GuardarCursoAsync(new GuardarCursoRequest(null, "Trabajo en caliente", null, "SST", true, null))).Id!.Value;
            cursoB = (await svc.GuardarCursoAsync(new GuardarCursoRequest(null, "Excel basico", null, "Ofimatica", true, null))).Id!.Value;
            cursoBorrador = (await svc.GuardarCursoAsync(new GuardarCursoRequest(null, "En construccion", null, null, false, null))).Id!.Value;
            var m = (await svc.GuardarModuloAsync(new GuardarModuloRequest(cursoA, null, "Unico", null))).Id!.Value;
            l1 = (await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m, null, "L1", null, CursoLeccionTipo.Texto, null, "<p>uno</p>", null))).Id!.Value;
            l2 = (await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m, null, "L2", null, CursoLeccionTipo.Texto, null, "<p>dos</p>", null))).Id!.Value;

            // A: obligatorio para la dependencia (alcanza al cargo hijo) con limite AYER y para Ana con limite mas tarde.
            var ayer = new DateOnly(2026, 11, 21);
            Assert.True((await svc.AgregarAsignacionAsync(new AgregarAsignacionRequest(cursoA, CursoAsignacionDestino.Unidad, null, dependencia, ayer))).Ok);
            Assert.True((await svc.AgregarAsignacionAsync(new AgregarAsignacionRequest(cursoA, CursoAsignacionDestino.Usuario, ana.TenantUserId, null, new DateOnly(2026, 12, 31)))).Ok);
            Assert.False((await svc.AgregarAsignacionAsync(new AgregarAsignacionRequest(cursoA, CursoAsignacionDestino.Usuario, ana.TenantUserId, null, null))).Ok); // duplicada
            Assert.False((await svc.AgregarAsignacionAsync(new AgregarAsignacionRequest(cursoA, CursoAsignacionDestino.Unidad, null, Guid.NewGuid(), null))).Ok);
            // B: obligatorio para todos, sin limite.
            Assert.True((await svc.AgregarAsignacionAsync(new AgregarAsignacionRequest(cursoB, CursoAsignacionDestino.Todos, null, null, null))).Ok);

            var asign = await svc.ListarAsignacionesAsync(cursoA);
            Assert.Equal(2, asign.Count);
            Assert.Equal("Operaciones", asign.Single(a => a.Destino == CursoAsignacionDestino.Unidad).DestinoNombre);
            Assert.Equal(1, asign.Single(a => a.Destino == CursoAsignacionDestino.Unidad).Personas);
        }

        // Ana (miembro del cargo hijo): A obligatorio con la fecha MAS CERCANA (ayer) y vencido; B obligatorio; sin borrador.
        await using (var ctx = _fixture.CreateContext(t))
        {
            var cat = await Svc(ctx, t, ana.PlatformUserId, new MemFiles()).CatalogoAsync();
            Assert.Equal(new[] { cursoA, cursoB }.OrderBy(x => x), cat.Select(c => c.Id).OrderBy(x => x));
            var a = cat.Single(c => c.Id == cursoA);
            Assert.True(a.Obligatorio);
            Assert.Equal(new DateOnly(2026, 11, 21), a.FechaLimite);
            Assert.True(a.Vencido);
            Assert.Equal(2, a.Lecciones);
            Assert.True(cat.Single(c => c.Id == cursoB).Obligatorio);
            Assert.Equal(cursoA, cat[0].Id); // los obligatorios con limite van primero
        }

        // Beto (sin unidad): A no es obligatorio para el; B si (todos).
        await using (var ctx = _fixture.CreateContext(t))
        {
            var cat = await Svc(ctx, t, beto.PlatformUserId, new MemFiles()).CatalogoAsync();
            Assert.False(cat.Single(c => c.Id == cursoA).Obligatorio);
            Assert.True(cat.Single(c => c.Id == cursoB).Obligatorio);
        }

        // Avance de Ana: abre, marca L1 (50 %), marca L2 (100 % completado); repetir no duplica.
        await using (var ctx = _fixture.CreateContext(t))
        {
            var svc = Svc(ctx, t, ana.PlatformUserId, new MemFiles());
            Assert.Null(await svc.VerCursoAsync(cursoBorrador, puedeEditar: false));
            Assert.NotNull(await svc.VerCursoAsync(cursoBorrador, puedeEditar: true));
            await svc.RegistrarAperturaAsync(cursoA, l1, puedeEditar: false);
            var a1 = await svc.MarcarVistaAsync(l1, puedeEditar: false);
            Assert.Equal(50, a1!.Porcentaje);
            Assert.False(a1.Completado);
            var a2 = await svc.MarcarVistaAsync(l2, puedeEditar: false);
            Assert.Equal(100, a2!.Porcentaje);
            Assert.True(a2.Completado);
            Assert.Equal(2, (await svc.MarcarVistaAsync(l2, puedeEditar: false))!.LeccionesVistas);
            var vista = (await svc.VerCursoAsync(cursoA, puedeEditar: false))!;
            Assert.Equal(new[] { l1, l2 }.OrderBy(x => x), vista.Vistas.OrderBy(x => x));
            Assert.True(vista.Completado);
            Assert.Equal(l2, vista.UltimaLeccionId);
            var cat = await svc.CatalogoAsync();
            Assert.True(cat.Single(c => c.Id == cursoA).Completado);
            Assert.False(cat.Single(c => c.Id == cursoA).Vencido); // completado: ya no cuenta como vencido
        }

        // El admin agrega una leccion: el avance de Ana se recalcula (67 %, ya no completo).
        await using (var ctx = _fixture.CreateContext(t))
        {
            var svc = Svc(ctx, t, admin.PlatformUserId, new MemFiles());
            var modulo = (await svc.DetalleAsync(cursoA))!.Modulos[0].Id;
            Assert.True((await svc.GuardarLeccionAsync(new GuardarLeccionRequest(modulo, null, "L3", null, CursoLeccionTipo.Texto, null, "<p>tres</p>", null))).Ok);
        }
        await using (var ctx = _fixture.CreateContext(t))
        {
            var p = await ctx.CursoProgresos.AsNoTracking().SingleAsync(x => x.CursoId == cursoA && x.TenantUserId == ana.TenantUserId);
            Assert.Equal(67, p.Porcentaje);
            Assert.Null(p.FechaCompletado);
        }
    }

    [Fact]
    public async Task Aislamiento_OtroTenant_Wizard_YBorradoLogico()
    {
        var a = await SeedTenantAsync("Formacion TA");
        var b = await SeedTenantAsync("Formacion TB");
        var adminA = await SeedUserAsync(a, "adminA");
        var userB = await SeedUserAsync(b, "userB");
        var files = new MemFiles();
        Guid cursoId, lPdf, lWiz;
        await using (var ctx = _fixture.CreateContext(a))
        {
            var svc = Svc(ctx, a, adminA.PlatformUserId, files);
            cursoId = (await svc.GuardarCursoAsync(new GuardarCursoRequest(null, "Privado A", null, null, true, null))).Id!.Value;
            var m = (await svc.GuardarModuloAsync(new GuardarModuloRequest(cursoId, null, "M", null))).Id!.Value;
            lPdf = (await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m, null, "PDF", null, CursoLeccionTipo.Pdf, null, null, null))).Id!.Value;
            Assert.True((await svc.SubirArchivoLeccionAsync(lPdf, Bytes(50), "a.pdf", "application/pdf")).Ok);
            lWiz = (await svc.GuardarLeccionAsync(new GuardarLeccionRequest(m, null, "W", null, CursoLeccionTipo.WizardHtml, null, null, null))).Id!.Value;
            Assert.True((await svc.SubirArchivoLeccionAsync(lWiz, Zip(("site/index.html", "<p>x</p>"), ("site/app.js", "1")), "w.zip", "application/zip")).Ok);
            Assert.NotNull(await svc.ResolverRecursoAsync(lPdf, puedeEditar: false));
            var w = await svc.ResolverWizardAsync(lWiz, puedeEditar: false);
            Assert.Equal("site/", w!.RootPrefix);
            Assert.Equal("index.html", w.EntryPath);
            Assert.Null(await svc.ResolverRecursoAsync(lWiz, puedeEditar: false)); // el wizard no sale por /recurso
        }

        // Tenant B: no ve el curso, ni el recurso, ni el wizard.
        await using (var ctx = _fixture.CreateContext(b))
        {
            var svc = Svc(ctx, b, userB.PlatformUserId, files);
            Assert.Empty(await svc.CatalogoAsync());
            Assert.Empty(await svc.ListarCursosAdminAsync());
            Assert.Null(await svc.VerCursoAsync(cursoId, puedeEditar: true));
            Assert.Null(await svc.ResolverRecursoAsync(lPdf, puedeEditar: true));
            Assert.Null(await svc.ResolverWizardAsync(lWiz, puedeEditar: true));
            Assert.Null(await svc.MarcarVistaAsync(lPdf, puedeEditar: true));
        }

        // Enlace firmado del wizard: solo con el tenant correcto (sin tenant en contexto).
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var svc = Svc(ctx, null, null, files);
            Assert.NotNull(await svc.ResolverWizardParaTenantAsync(lWiz, a));
            Assert.Null(await svc.ResolverWizardParaTenantAsync(lWiz, b));
        }

        // Borrado logico: sale del catalogo y de la administracion; la fila sigue en la BD.
        await using (var ctx = _fixture.CreateContext(a))
        {
            var svc = Svc(ctx, a, adminA.PlatformUserId, files);
            Assert.True((await svc.EliminarCursoAsync(cursoId)).Ok);
            Assert.Empty(await svc.CatalogoAsync());
            Assert.Empty(await svc.ListarCursosAdminAsync());
            Assert.Null(await svc.ResolverRecursoAsync(lPdf, puedeEditar: true));
        }
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var row = await ctx.Cursos.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == cursoId);
            Assert.True(row.IsDeleted);
            Assert.NotNull(row.DeletedByTenantUserId);
            var svc = Svc(ctx, null, null, files);
            Assert.Null(await svc.ResolverWizardParaTenantAsync(lWiz, a)); // curso borrado: el enlace deja de servir
        }
    }

    [Theory]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/abcDEF12345", "https://www.youtube-nocookie.com/embed/abcDEF12345")]
    [InlineData("https://vimeo.com/123456789", "https://player.vimeo.com/video/123456789")]
    [InlineData("https://player.vimeo.com/video/76979871", "https://player.vimeo.com/video/76979871")]
    [InlineData("https://evil.com/watch?v=dQw4w9WgXcQ", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("https://www.youtube.com/watch?v=<script>", null)]
    public void EnlacesDeVideo_SoloYoutubeYVimeo(string url, string? esperado)
        => Assert.Equal(esperado, FormacionVideoLinks.ToEmbedUrl(url));

    // ---- Helpers ----

    private sealed record Usuario(Guid PlatformUserId, Guid TenantUserId);

    private static FormacionService Svc(EcorexDbContext ctx, Guid? tenantId, Guid? platformUserId, IFileStorageService files)
        => new(ctx, new TestTenantContext(tenantId, platformUserId), files, new FixedClock(Ahora));

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Usuario> SeedUserAsync(Guid tenantId, string nombre)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var p = new PlatformUser { Email = $"{nombre}-{Guid.NewGuid():N}@formacion.test", DisplayName = nombre, EmailVerified = true, Status = PlatformUserStatus.Active };
        ctx.PlatformUsers.Add(p);
        var tu = new TenantUser { TenantId = tenantId, PlatformUserId = p.Id, Email = p.Email, TenantRole = TenantRole.Advisor, Status = PlatformUserStatus.Active };
        ctx.TenantUsers.Add(tu);
        await ctx.SaveChangesAsync();
        return new Usuario(p.Id, tu.Id);
    }

    private static MemoryStream Bytes(int n) => new(Enumerable.Range(0, n).Select(i => (byte)(i % 251)).ToArray());

    private static MemoryStream Zip(params (string Path, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var s = zip.CreateEntry(path).Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>Gestor de archivos en memoria (los metodos por stream usan la implementacion por defecto).</summary>
    private sealed class MemFiles : IFileStorageService
    {
        private readonly Dictionary<Guid, (StoredFileDto Dto, byte[] Bytes)> _files = new();
        public int Count => _files.Count;
        public bool Exists(Guid id) => _files.ContainsKey(id);

        public Task<StoredFileDto> SaveAsync(FileStorageSaveRequest request, CancellationToken ct = default)
        {
            var dto = new StoredFileDto(Guid.CreateVersion7(), request.Module, request.Ref1, request.Ref2, request.FileName,
                request.ContentType, request.Content.LongLength, DateTimeOffset.UtcNow, "Mem");
            _files[dto.Id] = (dto, request.Content);
            return Task.FromResult(dto);
        }
        public Task<IReadOnlyList<StoredFileDto>> ListAsync(string module, string? ref1 = null, string? ref2 = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StoredFileDto>>(_files.Values.Select(f => f.Dto).ToList());
        public Task<StoredFileDto?> GetAsync(Guid fileId, CancellationToken ct = default)
            => Task.FromResult(_files.TryGetValue(fileId, out var f) ? f.Dto : null);
        public Task<FileStorageContent?> ReadAsync(Guid fileId, CancellationToken ct = default)
            => Task.FromResult(_files.TryGetValue(fileId, out var f) ? new FileStorageContent(f.Bytes, f.Dto.ContentType ?? "", f.Dto.FileName) : null);
        public Task<FileStorageContent?> ReadForTenantAsync(Guid fileId, Guid tenantId, CancellationToken ct = default) => ReadAsync(fileId, ct);
        public Task<bool> DeleteAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult(_files.Remove(fileId));
        public Task<StoredFileDto?> FindAsync(string module, string? ref1, string? ref2, string fileName, CancellationToken ct = default)
            => Task.FromResult<StoredFileDto?>(null);
        public Task<IReadOnlyCollection<string>> ListRefsWithFilesAsync(string module, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class FormacionModuleTests_Postgres : FormacionModuleTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public FormacionModuleTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class FormacionModuleTests_SqlServer : FormacionModuleTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public FormacionModuleTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
