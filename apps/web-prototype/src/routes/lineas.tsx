import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card, StatTile } from "@/components/dashboard/ModulePage";
import { Smartphone, QrCode, RefreshCw } from "lucide-react";

export const Route = createFileRoute("/lineas")({
  head: () => ({
    meta: [
      { title: "Líneas WhatsApp · ECOREX.tareas" },
      { name: "description", content: "Monitoreo y conexión de líneas WhatsApp vía Evolution API." },
    ],
  }),
  component: Page,
});

const lines = [
  { name: "Ventas — Vuelos", num: "+57 320 ··· 4521", state: "online", instance: "evo-andes-01", msgsToday: 142, uptime: "99.8%" },
  { name: "Paquetes vacacionales", num: "+57 311 ··· 8809", state: "online", instance: "evo-andes-02", msgsToday: 87, uptime: "99.4%" },
  { name: "Servicio post-venta", num: "+57 304 ··· 2244", state: "warn", instance: "evo-andes-03", msgsToday: 23, uptime: "92.1%" },
];

function Page() {
  return (
    <ModulePage
      icon={Smartphone}
      eyebrow="Infraestructura"
      title="Líneas WhatsApp"
      description="Conexión con Evolution API por tenant: estado, instancias, escaneo de QR y reconexión automática."
      actions={
        <button className="h-10 px-4 rounded-lg bg-foreground text-background text-sm font-semibold inline-flex items-center gap-2 shadow-soft">
          <QrCode className="size-4" /> Conectar línea
        </button>
      }
    >
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatTile label="Líneas activas" value="3" hint="De 5 permitidas (Plan Pro)" tone="success" />
        <StatTile label="Mensajes hoy" value="252" hint="Entrada + salida" tone="primary" />
        <StatTile label="Caídas última semana" value="2" hint="Auto-reconectadas" tone="gold" />
        <StatTile label="Uptime promedio" value="97.1%" />
      </div>

      <Card className="overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-[10px] uppercase tracking-wider text-muted-foreground font-bold">
            <tr>
              <th className="text-left px-4 py-3">Línea</th>
              <th className="text-left px-4 py-3">Instancia Evolution</th>
              <th className="text-left px-4 py-3">Estado</th>
              <th className="text-right px-4 py-3">Msgs hoy</th>
              <th className="text-right px-4 py-3">Uptime</th>
              <th className="text-right px-4 py-3">Acción</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {lines.map((l) => (
              <tr key={l.num} className="hover:bg-muted/30">
                <td className="px-4 py-3">
                  <div className="font-semibold">{l.name}</div>
                  <div className="text-[11px] text-muted-foreground">{l.num}</div>
                </td>
                <td className="px-4 py-3 text-xs text-muted-foreground font-mono">{l.instance}</td>
                <td className="px-4 py-3">
                  <span className={`text-[10px] font-bold px-2 py-1 rounded-full inline-flex items-center gap-1.5 ${l.state === "online" ? "bg-success/15 text-success" : "bg-gold/15 text-gold-foreground"}`}>
                    <span className={`size-1.5 rounded-full ${l.state === "online" ? "bg-success" : "bg-gold"}`} />
                    {l.state === "online" ? "Conectada" : "Inestable"}
                  </span>
                </td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">{l.msgsToday}</td>
                <td className="px-4 py-3 text-right text-xs tabular-nums">{l.uptime}</td>
                <td className="px-4 py-3 text-right">
                  <button className="size-8 rounded-md hover:bg-muted grid place-items-center inline-flex"><RefreshCw className="size-3.5 text-muted-foreground" /></button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </ModulePage>
  );
}