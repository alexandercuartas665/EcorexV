import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card, StatTile } from "@/components/dashboard/ModulePage";
import { Receipt } from "lucide-react";

export const Route = createFileRoute("/admin/facturacion")({
  head: () => ({
    meta: [
      { title: "Facturación · ECOREX Super Admin" },
      { name: "description", content: "Cobros, recaudos vía Wompi y estado financiero de los tenants." },
    ],
  }),
  component: Page,
});

const invoices = [
  { id: "INV-2918", tenant: "Andes Travel", amount: "$ 890K", date: "10 May", status: "pagada" },
  { id: "INV-2917", tenant: "Bogotá Travel Group", amount: "$ 2.4M", date: "10 May", status: "pagada" },
  { id: "INV-2916", tenant: "MarSol Tours", amount: "$ 720K", date: "08 May", status: "pendiente" },
  { id: "INV-2915", tenant: "Pacífico Sun", amount: "$ 240K", date: "05 May", status: "vencida" },
];

const badge: Record<string, string> = {
  pagada: "bg-success/15 text-success",
  pendiente: "bg-gold/15 text-gold-foreground",
  vencida: "bg-destructive/10 text-destructive",
};

function Page() {
  return (
    <ModulePage
      icon={Receipt}
      eyebrow="Super Admin SaaS"
      title="Facturación y recaudo"
      description="Cobros recurrentes a tenants vía Wompi, consulta de estado por agencia y alertas de morosidad."
    >
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatTile label="Recaudado mes" value="$ 32.4M" hint="84% del MRR objetivo" tone="success" />
        <StatTile label="Pendiente" value="$ 4.8M" hint="6 facturas abiertas" tone="gold" />
        <StatTile label="Vencido" value="$ 1.2M" hint="2 tenants en mora" tone="danger" />
        <StatTile label="Tasa de cobro" value="93%" tone="primary" />
      </div>

      <Card className="overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-[10px] uppercase tracking-wider text-muted-foreground font-bold">
            <tr>
              <th className="text-left px-4 py-3">Factura</th>
              <th className="text-left px-4 py-3">Tenant</th>
              <th className="text-right px-4 py-3">Monto</th>
              <th className="text-left px-4 py-3">Fecha</th>
              <th className="text-left px-4 py-3">Estado</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {invoices.map((i) => (
              <tr key={i.id} className="hover:bg-muted/30">
                <td className="px-4 py-3 font-mono text-xs">{i.id}</td>
                <td className="px-4 py-3 font-semibold">{i.tenant}</td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">{i.amount}</td>
                <td className="px-4 py-3 text-xs text-muted-foreground">{i.date}</td>
                <td className="px-4 py-3"><span className={`text-[10px] font-bold px-2 py-1 rounded-full uppercase ${badge[i.status]}`}>{i.status}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </ModulePage>
  );
}