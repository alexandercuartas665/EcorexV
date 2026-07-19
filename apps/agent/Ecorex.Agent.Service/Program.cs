using Ecorex.Agent.Core.Services;
using Ecorex.Agent.Service;
using Ecorex.Contracts.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;

// Origen del Visor de eventos. Lo REGISTRA el instalador (crear un origen exige privilegio y hacerlo
// al arrancar seria tarde y fragil). Si no existe, el proveedor de EventLog no escribe nada y no
// avisa: por eso el instalador es quien se encarga.
const string EventSourceName = "ECOREX Agente";

// Servicio Windows del Agente Conector On-Prem (ADR-0039, Ola 5b).
//
// El MISMO binario corre de dos formas, a proposito:
//  - como Servicio Windows (LocalSystem), que es el modo de produccion;
//  - como consola (`Ecorex.Agent.Service.exe` a mano), para diagnosticar sin instalar nada.
// `AddWindowsService` detecta el caso y `UseWindowsService` no estorba en consola.

// Configuracion de la identidad. Vive AQUI y no en la colmena porque el dueno de la boveda es el
// servicio (ADR-0039 s1-s2): escribirla exige permiso sobre %ProgramData%\Ecorex\Agent, cuyo ACL es
// SYSTEM + Administradores. Lo invoca el instalador (Ola 5d) y sirve para diagnostico:
//   Ecorex.Agent.Service.exe --save-config <clientId> <hubUrl> [secreto]   (consola de administrador)
if (args.Length >= 3 && string.Equals(args[0], "--save-config", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        // La config guarda la URL COMPLETA del hub porque el cliente SignalR se conecta a ella tal
        // cual. Un operador (o un instalador) escribe naturalmente la URL BASE del servidor, y ese
        // desliz se manifiesta como un "no pude conectar" mudo. Se normaliza aqui, en el borde.
        var hubUrl = args[2].TrimEnd('/');
        if (!hubUrl.EndsWith(AgentProtocol.HubRoute, StringComparison.OrdinalIgnoreCase))
        {
            hubUrl += AgentProtocol.HubRoute;
        }

        new DpapiConfigStore().Save(new AgentConfig(args[1], hubUrl, args.Length > 3 ? args[3] : string.Empty));
        Console.WriteLine($"Configuracion guardada en la boveda: {AgentVault.Dir}");
        Console.WriteLine($"  ClientId: {args[1]}");
        Console.WriteLine($"  Hub:      {hubUrl}");
        Console.WriteLine($"  Secreto:  {(args.Length > 3 && args[3].Length > 0 ? "si" : "NO (se conectara anonimo)")}");
        return 0;
    }
    catch (UnauthorizedAccessException)
    {
        Console.Error.WriteLine(
            $"Sin permiso para escribir la boveda ({AgentVault.Dir}). Ejecute este comando en una consola de ADMINISTRADOR.");
        return 2;
    }
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "EcorexAgent");
builder.Services.AddHostedService<AgentWorker>();

// En servicio no hay consola donde mirar: el log va al Visor de eventos. En consola, a la consola.
if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Logging.AddEventLog(settings => settings.SourceName = EventSourceName);

    // Sin esto el Visor de eventos queda MUDO (verificado el 2026-07-16): el proveedor de EventLog
    // filtra en Warning por defecto, asi que "Conectado a X como Y" -la linea que mas sirve para
    // soporte- se descartaba. Es la UNICA ventana al servicio en produccion; que cuente lo que hace.
    builder.Logging.AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Information);
}

// Diagnostico SIEMPRE a archivo (corra suelto, en consola o como servicio): deja una copia legible
// del ciclo de conexion en %PUBLIC%\Documents\ecorex-agent-diag.log. Es la ventana que faltaba cuando
// el canal queda Offline y no hay consola a la vista. Ruta fija para no adivinar donde mirar.
builder.Logging.AddProvider(new FileLoggerProvider(FileLoggerProvider.DefaultPath));

var host = builder.Build();
await host.RunAsync();
return 0;
