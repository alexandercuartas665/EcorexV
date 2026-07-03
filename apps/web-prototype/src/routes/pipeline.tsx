import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card, StatTile } from "@/components/dashboard/ModulePage";
import { KanbanSquare, Plus, Filter } from "lucide-react";

export const Route = createFileRoute("/pipeline")({
  head: () => ({
    meta: [
      { title: "Pipeline · ECOREX.tareas" },
      { name: "description", content: "Tablero Kanban comercial: Lead, Cotización, Inquietudes, Cierre y Seguimiento." },
    ],
  }),
  component: PipelinePage,
});

const columns = [
  { name: "Lead", count: 84, value: "$ 56M", color: "border-t-primary/70" },
  { name: "Cotización", count: 37, value: "$ 92M", color: "border-t-primary" },
  { name: "Inquietudes", count: 21, value: "$ 48M", color: "border-t-gold" },
  { name: "Cierre", count: 18, value: "$ 38M", color: "border-t-success" },
  { name: "Seguimiento", count: 24, value: "$ 14M", color: "border-t-muted-foreground/50" },
];

const sample: Record<string, { client: string; pkg: string; pax: number; price: string; advisor: string }[]> = {
  Lead: [
    { client: "María Ortiz", pkg: "Cartagena 4D/3N", pax: 2, price: "$ 2.8M", advisor: "CR" },
    { client: "Juan Pérez", pkg: "San Andrés 5D", pax: 4, price: "$ 6.4M", advisor: "DS" },
    { client: "Familia Gómez", pkg: "Cancún todo incluido", pax: 5, price: "$ 18M", advisor: "CR" },
  ],
  Cotización: [
    { client: "Andrés Vega", pkg: "Bali 12D", pax: 2, price: "$ 22M", advisor: "DS" },
    { client: "Lucía Mora", pkg: "Vuelo BOG-MIA", pax: 1, price: "$ 1.9M", advisor: "JM" },
  ],
  Inquietudes: [
    { client: "Carlos Niño", pkg: "Europa 15D", pax: 3, price: "$ 38M", advisor: "CR" },
  ],
  Cierre: [
    { client: "Diana Soto", pkg: "Punta Cana 7D", pax: 2, price: "$ 9.4M", advisor: "JM" },
  ],
  Seguimiento: [
    { client: "Sergio Ríos", pkg: "Crucero Caribe", pax: 2, price: "$ 12M", advisor: "DS" },
  ],
};

function PipelinePage() {
  return (
    <ModulePage
      icon={KanbanSquare}
      eyebrow="Operación Comercial"
      title="Pipeline de ventas"
      description="Drag & drop entre etapas, formularios dinámicos por columna y alertas por tiempo de inactividad."
      actions={
        <>
          <button className="h-10 px-4 rounded-lg border border-border bg-card hover:bg-muted text-sm font-semibold inline-flex items-center gap-2">
            <Filter className="size-4" /> Filtros
          </button>
          <button className="h-10 px-4 rounded-lg bg-foreground text-background hover:bg-foreground/90 text-sm font-semibold inline-flex items-center gap-2 shadow-soft">
            <Plus className="size-4" /> Nuevo lead
          </button>
        </>
      }
    >
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-3">
        {columns.map((c) => (
          <StatTile key={c.name} label={c.name} value={String(c.count)} hint={c.value} tone="primary" />
        ))}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-5 gap-4">
        {columns.map((c) => (
          <Card key={c.name} className={`border-t-4 ${c.color} p-4`}>
            <header className="flex items-center justify-between mb-3">
              <div>
                <div className="text-sm font-bold">{c.name}</div>
                <div className="text-[11px] text-muted-foreground">{c.count} leads · {c.value}</div>
              </div>
              <button className="size-7 rounded-md hover:bg-muted grid place-items-center">
                <Plus className="size-4 text-muted-foreground" />
              </button>
            </header>
            <ul className="space-y-2.5">
              {(sample[c.name] || []).map((l, i) => (
                <li key={i} className="rounded-lg border border-border bg-background p-3 hover:border-primary/40 transition-colors cursor-pointer">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="text-sm font-semibold truncate">{l.client}</div>
                      <div className="text-[11px] text-muted-foreground truncate">{l.pkg}</div>
                    </div>
                    <div className="size-6 rounded-full bg-primary/15 text-primary text-[10px] font-bold grid place-items-center shrink-0">
                      {l.advisor}
                    </div>
                  </div>
                  <div className="mt-2 flex items-center justify-between text-[11px]">
                    <span className="text-muted-foreground">{l.pax} pax</span>
                    <span className="font-bold tabular-nums">{l.price}</span>
                  </div>
                </li>
              ))}
            </ul>
          </Card>
        ))}
      </div>
    </ModulePage>
  );
}