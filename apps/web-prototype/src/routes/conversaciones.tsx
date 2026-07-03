import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card } from "@/components/dashboard/ModulePage";
import { MessagesSquare, Send, Search, Bot } from "lucide-react";

export const Route = createFileRoute("/conversaciones")({
  head: () => ({
    meta: [
      { title: "Conversaciones · ECOREX.tareas" },
      { name: "description", content: "Bandeja unificada WhatsApp con sugerencias del copiloto IA y trazabilidad por asesor." },
    ],
  }),
  component: Page,
});

const chats = [
  { name: "María Ortiz", last: "¿Tienen cupos para Cartagena en julio?", time: "9:42", unread: 2, line: "Vuelos" },
  { name: "Juan Pérez", last: "Perfecto, mándame la cotización", time: "9:15", unread: 0, line: "Paquetes" },
  { name: "Familia Gómez", last: "Ya pagué la separación", time: "Ayer", unread: 1, line: "Post-venta" },
  { name: "Andrés Vega", last: "¿Incluye traslados al hotel?", time: "Ayer", unread: 0, line: "Paquetes" },
  { name: "Lucía Mora", last: "Confirmen el itinerario por favor", time: "Lun", unread: 3, line: "Vuelos" },
];

function Page() {
  return (
    <ModulePage
      icon={MessagesSquare}
      eyebrow="Comercial Conversacional"
      title="Bandeja unificada"
      description="WhatsApp multi-línea con asignación de asesores, etiquetas y sugerencias del copiloto."
    >
      <div className="grid grid-cols-12 gap-4 h-[calc(100vh-280px)] min-h-[500px]">
        <Card className="col-span-12 lg:col-span-4 flex flex-col overflow-hidden">
          <div className="p-3 border-b border-border">
            <div className="relative">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 size-3.5 text-muted-foreground" />
              <input placeholder="Buscar chat…" className="w-full h-9 rounded-lg bg-muted border border-border pl-8 pr-2 text-xs focus:outline-none focus:ring-2 focus:ring-ring/40" />
            </div>
          </div>
          <ul className="flex-1 overflow-y-auto divide-y divide-border">
            {chats.map((c, i) => (
              <li key={i} className={`p-3 hover:bg-muted/40 cursor-pointer ${i === 0 ? "bg-primary-soft/40" : ""}`}>
                <div className="flex items-start gap-2.5">
                  <div className="size-9 rounded-full bg-gradient-to-br from-primary to-primary/60 text-primary-foreground text-[11px] font-bold grid place-items-center shrink-0">
                    {c.name.split(" ").map(n => n[0]).slice(0, 2).join("")}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center justify-between">
                      <div className="text-sm font-semibold truncate">{c.name}</div>
                      <span className="text-[10px] text-muted-foreground shrink-0">{c.time}</span>
                    </div>
                    <div className="text-[11px] text-muted-foreground truncate">{c.last}</div>
                    <div className="mt-1 flex items-center gap-1.5">
                      <span className="text-[9px] uppercase tracking-wider font-bold px-1.5 py-0.5 rounded bg-muted text-muted-foreground">{c.line}</span>
                      {c.unread > 0 && (
                        <span className="text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-destructive text-destructive-foreground">{c.unread}</span>
                      )}
                    </div>
                  </div>
                </div>
              </li>
            ))}
          </ul>
        </Card>

        <Card className="col-span-12 lg:col-span-5 flex flex-col overflow-hidden">
          <header className="p-4 border-b border-border flex items-center gap-3">
            <div className="size-10 rounded-full bg-gradient-to-br from-primary to-primary/60 text-primary-foreground text-xs font-bold grid place-items-center">MO</div>
            <div className="flex-1">
              <div className="text-sm font-bold">María Ortiz</div>
              <div className="text-[11px] text-muted-foreground">+57 312 ··· 9821 · Línea Vuelos</div>
            </div>
            <span className="text-[10px] font-bold px-2 py-1 rounded-full bg-gold/15 text-gold-foreground">Inquietudes</span>
          </header>
          <div className="flex-1 overflow-y-auto p-4 space-y-3 bg-muted/30">
            <div className="max-w-[75%] rounded-2xl rounded-bl-sm bg-card border border-border p-3 text-sm">¿Tienen cupos para Cartagena en julio?</div>
            <div className="max-w-[75%] ml-auto rounded-2xl rounded-br-sm bg-primary text-primary-foreground p-3 text-sm">Hola María, sí tenemos disponibilidad del 15 al 22 de julio. ¿Para cuántas personas?</div>
            <div className="max-w-[75%] rounded-2xl rounded-bl-sm bg-card border border-border p-3 text-sm">Seríamos 2 adultos. ¿Cuánto saldría con vuelo desde Bogotá?</div>
          </div>
          <div className="p-3 border-t border-border">
            <div className="rounded-xl border border-border bg-background flex items-end gap-2 p-2">
              <textarea rows={1} placeholder="Escribe un mensaje…" className="flex-1 bg-transparent text-sm focus:outline-none resize-none" />
              <button className="size-9 rounded-lg bg-primary text-primary-foreground grid place-items-center"><Send className="size-4" /></button>
            </div>
          </div>
        </Card>

        <Card className="col-span-12 lg:col-span-3 p-4 overflow-y-auto">
          <div className="flex items-center gap-2 mb-3">
            <div className="size-7 rounded-lg bg-gold grid place-items-center"><Bot className="size-4 text-gold-foreground" /></div>
            <span className="text-[10px] font-bold uppercase tracking-wider text-primary">Copiloto IA</span>
          </div>
          <h4 className="text-sm font-bold mb-2">Sugerencias para esta conversación</h4>
          <div className="space-y-2">
            <button className="w-full text-left rounded-lg border border-border bg-background p-2.5 hover:border-primary/40">
              <div className="text-xs font-semibold">Enviar cotización Cartagena 4D/3N</div>
              <div className="text-[11px] text-muted-foreground mt-0.5">2 adultos · vuelo BOG incluido · $ 2.8M</div>
            </button>
            <button className="w-full text-left rounded-lg border border-border bg-background p-2.5 hover:border-primary/40">
              <div className="text-xs font-semibold">Programar llamada de cierre</div>
              <div className="text-[11px] text-muted-foreground mt-0.5">Cliente con intención alta detectada.</div>
            </button>
          </div>
          <div className="mt-5 rounded-lg bg-primary-soft border border-primary/15 p-3 text-[11px] text-muted-foreground">
            <strong className="text-primary">Resumen IA:</strong> Cliente interesada en luna de miel, fecha flexible, presupuesto ~$3M, ya cotizó con otra agencia hace 2 días.
          </div>
        </Card>
      </div>
    </ModulePage>
  );
}