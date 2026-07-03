import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card, StatTile } from "@/components/dashboard/ModulePage";
import { UserSquare2, Plus, Download } from "lucide-react";

export const Route = createFileRoute("/leads")({
  head: () => ({
    meta: [
      { title: "Leads · ECOREX.tareas" },
      { name: "description", content: "Listado completo de leads con filtros, segmentos y asignación por asesor." },
    ],
  }),
  component: Page,
});

const leads = [
  { name: "María Ortiz", phone: "+57 312··· 9821", origin: "WhatsApp", stage: "Inquietudes", advisor: "Camila R.", value: "$ 2.8M", days: 3 },
  { name: "Juan Pérez", phone: "+57 311··· 4502", origin: "Web", stage: "Cotización", advisor: "Daniel S.", value: "$ 6.4M", days: 1 },
  { name: "Familia Gómez", phone: "+57 304··· 1188", origin: "WhatsApp", stage: "Lead", advisor: "Camila R.", value: "$ 18M", days: 0 },
  { name: "Andrés Vega", phone: "+57 320··· 7733", origin: "Instagram", stage: "Cotización", advisor: "Daniel S.", value: "$ 22M", days: 5 },
  { name: "Diana Soto", phone: "+57 316··· 2210", origin: "Referido", stage: "Cierre", advisor: "Juan M.", value: "$ 9.4M", days: 12 },
];

function Page() {
  return (
    <ModulePage
      icon={UserSquare2}
      eyebrow="Base Comercial"
      title="Leads"
      description="Vista tabular para gestión masiva, filtros por etapa, asesor, origen y tiempo de inactividad."
      actions={
        <>
          <button className="h-10 px-4 rounded-lg border border-border bg-card hover:bg-muted text-sm font-semibold inline-flex items-center gap-2">
            <Download className="size-4" /> Exportar
          </button>
          <button className="h-10 px-4 rounded-lg bg-foreground text-background hover:bg-foreground/90 text-sm font-semibold inline-flex items-center gap-2 shadow-soft">
            <Plus className="size-4" /> Nuevo lead
          </button>
        </>
      }
    >
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatTile label="Total leads" value="184" hint="Últimos 30 días" tone="primary" />
        <StatTile label="Nuevos hoy" value="14" hint="42 esta semana" tone="success" />
        <StatTile label="Sin asignar" value="6" hint="Reasignar a turno" tone="danger" />
        <StatTile label="Valor pipeline" value="$ 248M" hint="Proyección mes" tone="gold" />
      </div>

      <Card className="overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-[10px] uppercase tracking-wider text-muted-foreground font-bold">
            <tr>
              <th className="text-left px-4 py-3">Cliente</th>
              <th className="text-left px-4 py-3">Origen</th>
              <th className="text-left px-4 py-3">Etapa</th>
              <th className="text-left px-4 py-3">Asesor</th>
              <th className="text-right px-4 py-3">Valor</th>
              <th className="text-right px-4 py-3">Días</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {leads.map((l) => (
              <tr key={l.phone} className="hover:bg-muted/30 cursor-pointer">
                <td className="px-4 py-3">
                  <div className="font-semibold">{l.name}</div>
                  <div className="text-[11px] text-muted-foreground">{l.phone}</div>
                </td>
                <td className="px-4 py-3 text-xs text-muted-foreground">{l.origin}</td>
                <td className="px-4 py-3"><span className="text-[10px] font-bold px-2 py-1 rounded-full bg-primary-soft text-primary">{l.stage}</span></td>
                <td className="px-4 py-3 text-xs">{l.advisor}</td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">{l.value}</td>
                <td className="px-4 py-3 text-right text-xs text-muted-foreground tabular-nums">{l.days}d</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </ModulePage>
  );
}