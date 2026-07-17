using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Ecorex.Agent.Core.Ipc;

/// <summary>
/// Enmarcado de mensajes sobre un named pipe: un JSON por linea, UTF-8. Lo comparten el servidor
/// (servicio) y el cliente (colmena) para que el formato no pueda divergir entre los dos lados.
///
/// Los envios estan serializados con un semaforo porque el pipe es DUPLEX: el servicio puede empujar
/// un `browser-req` mientras responde un `state`, y dos escrituras solapadas entrelazarian las lineas
/// y romperian el JSON del otro lado.
/// </summary>
public sealed class IpcChannel(PipeStream pipe) : IDisposable
{
    private readonly StreamReader _reader = new(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);
    private readonly StreamWriter _writer = new(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public bool IsConnected => pipe.IsConnected;

    public async Task SendAsync(AgentIpc.Envelope envelope, CancellationToken ct = default)
    {
        await _sendGate.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync(AgentIpc.Serialize(envelope).AsMemory(), ct);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>Siguiente mensaje, o null si el otro extremo cerro el pipe.</summary>
    public async Task<AgentIpc.Envelope?> ReceiveAsync(CancellationToken ct = default)
    {
        var line = await _reader.ReadLineAsync(ct);
        if (line is null) { return null; }
        try
        {
            return AgentIpc.Deserialize<AgentIpc.Envelope>(line);
        }
        catch
        {
            return null; // linea corrupta: se trata como fin de conversacion
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _sendGate.Dispose();
    }
}
