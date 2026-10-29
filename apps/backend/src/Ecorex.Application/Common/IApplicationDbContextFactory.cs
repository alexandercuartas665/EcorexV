namespace Ecorex.Application.Common;

/// <summary>
/// Crea instancias EFIMERAS y PROPIAS de <see cref="IApplicationDbContext"/> para operaciones
/// de SOLO LECTURA que NO deben compartir el DbContext scoped del circuito Blazor.
///
/// Motivo: el DbContext scoped se comparte entre una pagina y sus componentes hijos (p.ej. un
/// modal de detalle de tarea abierto sobre una pagina). Si dos operaciones async se solapan sobre
/// la MISMA instancia de DbContext, EF Core lanza
/// "A second operation was started on this context instance before a previous operation completed".
/// Cada consulta de lectura toma su propio contexto (con 'await using') y asi nunca colisiona con
/// el trabajo en vuelo del contexto compartido.
///
/// El contexto creado respeta el proveedor activo (DAL dual PostgreSQL/SQL Server, ADR-001) y el
/// tenant del circuito (recibe el MISMO <c>ITenantContext</c>, por lo que el filtro global
/// multi-tenant sigue aplicando intacto). Es solo para LECTURA: para escrituras usar el
/// IApplicationDbContext inyectado (la unidad de trabajo del circuito, con su transaccion).
/// </summary>
public interface IApplicationDbContextFactory
{
    /// <summary>Crea un contexto propio; el llamador lo libera con <c>await using</c>.</summary>
    IScopedDbContext Create();
}

/// <summary>
/// Un <see cref="IApplicationDbContext"/> con vida propia (desechable). Se obtiene de
/// <see cref="IApplicationDbContextFactory.Create"/> y el llamador es duenio de liberarlo.
/// </summary>
public interface IScopedDbContext : IApplicationDbContext, IAsyncDisposable
{
}
