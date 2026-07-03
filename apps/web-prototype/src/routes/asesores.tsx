import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card, StatTile } from "@/components/dashboard/ModulePage";
import { Users, Plus } from "lucide-react";

export const Route = createFileRoute("/asesores")({
  head: () => ({
    meta: [
      { title: "Asesores · ECOREX.tareas" },
      { name: "description", content: "Gestión de asesores, turnos, líneas asignadas y performance comercial." },
    ],
  }),
  component: Page,
});

const team = [
  { name: "Camila Ruiz", role: "Senior Vuelos", chats: 28, closed: 9, status: "online" },
  { name: "Daniel Soto", role: "Paquetes", chats: 19, closed: 6, status: "online" },
  { name: "Juan Marín", role: "Post-venta", chats: 12, closed: 4, status: "busy" },
  { name: "Laura Pinzón", role: "Corporativo", chats: 14, closed: 3, status: "offline" },
];

function Page() {
  return (
    <ModulePage
      icon={Users}
      eyebrow="Equipo"
      title="Asesores comerciales"
      description="Supervisión en tiempo real de cargas de trabajo, SLA y tasa de cierre por asesor."
      actions={
        <button className="h-10 px-4 rounded-lg bg-foreground text-background text-sm font-semibold inline-flex items-center gap-2 shadow-soft">
          <Plus className="size-4" /> Invitar asesor
        </button>
      }
    >
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatTile label="Asesores activos" value="12" hint="3 en pausa" tone="primary" />
        <StatTile label="Chats en curso" value="73" hint="Promedio 6.1 por asesor" />
        <StatTile label="Tasa de cierre prom." value="12.4%" hint="+2.1 pts vs mes anterior" tone="success" />
        <StatTile label="SLA primera respuesta" value="4:12" hint="Meta: < 5min" tone="gold" />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        {team.map((m) => (
          <Card key={m.name} className="p-5">
            <div className="flex items-center gap-3">
              <div className="size-12 rounded-full bg-gradient-to-br from-primary to-primary/60 grid place-items-center text-primary-foreground font-bold">
                {m.name.split(" ").map(n => n[0]).slice(0, 2).join("")}
              </div>
              <div className="flex-1 min-w-0">
                <div className="font-bold text-sm truncate">{m.name}</div>
                <div className="text-[11px] text-muted-foreground truncate">{m.role}</div>
              </div>
              <span className={`size-2.5 rounded-full ${m.status === "online" ? "bg-success animate-pulse" : m.status === "busy" ? "bg-gold" : "bg-muted-foreground/40"}`} />
            </div>
            <div className="mt-4 grid grid-cols-2 gap-2 text-center">
              <div className="rounded-lg bg-muted/40 p-2">
                <div className="text-lg font-bold tabular-nums">{m.chats}</div>
                <div className="text-[10px] uppercase tracking-wider text-muted-foreground">chats</div>
              </div>
              <div className="rounded-lg bg-success/10 p-2">
                <div className="text-lg font-bold tabular-nums text-success">{m.closed}</div>
                <div className="text-[10px] uppercase tracking-wider text-muted-foreground">cierres</div>
              </div>
            </div>
          </Card>
        ))}
      </div>
    </ModulePage>
  );
}