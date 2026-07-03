import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card, StatTile } from "@/components/dashboard/ModulePage";
import { Zap, Plus, ArrowRight } from "lucide-react";

export const Route = createFileRoute("/automatizaciones")({
  head: () => ({
    meta: [
      { title: "Automatizaciones · ECOREX.tareas" },
      { name: "description", content: "Reglas y workflows event-driven para responder, asignar, alertar y cobrar automáticamente." },
    ],
  }),
  component: Page,
});

const rules = [
  { name: "Bienvenida nuevo lead", trigger: "Mensaje entrante sin lead", action: "Crear lead + responder plantilla", runs: 142, active: true },
  { name: "Alerta sin respuesta 30min", trigger: "Chat sin respuesta > 30min", action: "Notificar supervisor", runs: 38, active: true },
  { name: "Reasignación nocturna", trigger: "Chat entra entre 22:00-06:00", action: "Asignar a turno noche", runs: 87, active: true },
  { name: "Cotización aprobada → pago", trigger: "Lead pasa a Cierre", action: "Generar link Wompi", runs: 23, active: false },
];

function Page() {
  return (
    <ModulePage
      icon={Zap}
      eyebrow="Workflows"
      title="Automatizaciones"
      description="Reglas event-driven sobre el embudo y los chats. Cada tenant define sus propios disparadores y acciones."
      actions={
        <button className="h-10 px-4 rounded-lg bg-foreground text-background text-sm font-semibold inline-flex items-center gap-2 shadow-soft">
          <Plus className="size-4" /> Nueva regla
        </button>
      }
    >
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatTile label="Reglas activas" value="6" hint="2 pausadas" tone="primary" />
        <StatTile label="Ejecuciones hoy" value="298" tone="success" />
        <StatTile label="Tiempo ahorrado" value="14h" hint="Estimado esta semana" tone="gold" />
        <StatTile label="Errores" value="0" hint="Últimas 24h" />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {rules.map((r) => (
          <Card key={r.name} className="p-5">
            <div className="flex items-center justify-between">
              <h3 className="font-bold text-sm">{r.name}</h3>
              <span className={`text-[10px] font-bold px-2 py-1 rounded-full ${r.active ? "bg-success/15 text-success" : "bg-muted text-muted-foreground"}`}>
                {r.active ? "Activa" : "Pausada"}
              </span>
            </div>
            <div className="mt-4 flex items-center gap-2 text-xs">
              <span className="rounded-md px-2.5 py-1.5 bg-primary-soft text-primary font-semibold">{r.trigger}</span>
              <ArrowRight className="size-3.5 text-muted-foreground" />
              <span className="rounded-md px-2.5 py-1.5 bg-gold/15 text-gold-foreground font-semibold">{r.action}</span>
            </div>
            <div className="mt-4 pt-4 border-t border-border text-xs text-muted-foreground">{r.runs} ejecuciones · últimos 30 días</div>
          </Card>
        ))}
      </div>
    </ModulePage>
  );
}