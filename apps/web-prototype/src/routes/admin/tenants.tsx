import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card, StatTile } from "@/components/dashboard/ModulePage";
import { Building2, Plus } from "lucide-react";

export const Route = createFileRoute("/admin/tenants")({
  head: () => ({
    meta: [
      { title: "Tenants · ECOREX Super Admin" },
      { name: "description", content: "Gestión maestra de agencias clientes del SaaS: planes, estado contractual y consumo." },
    ],
  }),
  component: Page,
});

const tenants = [
  { name: "Andes Travel", plan: "Pro", users: 12, lines: 3, status: "activo", mrr: "$ 890K" },
  { name: "MarSol Tours", plan: "Pro", users: 8, lines: 2, status: "activo", mrr: "$ 720K" },
  { name: "Caribe Vuela", plan: "Starter", users: 3, lines: 1, status: "trial", mrr: "$ 0" },
  { name: "Bogotá Travel Group", plan: "Enterprise", users: 28, lines: 8, status: "activo", mrr: "$ 2.4M" },
  { name: "Pacífico Sun", plan: "Starter", users: 4, lines: 1, status: "suspendido", mrr: "$ 240K" },
];

const badge: Record<string, string> = {
  activo: "bg-success/15 text-success",
  trial: "bg-gold/15 text-gold-foreground",
  suspendido: "bg-destructive/10 text-destructive",
};

function Page() {
  return (
    <ModulePage
      icon={Building2}
      eyebrow="Super Admin SaaS"
      title="Tenants"
      description="Cada tenant es una agencia turística aislada con sus propios usuarios, líneas WhatsApp, pipelines y datos."
      actions={
        <button className="h-10 px-4 rounded-lg bg-foreground text-background text-sm font-semibold inline-flex items-center gap-2 shadow-soft">
          <Plus className="size-4" /> Crear tenant
        </button>
      }
    >
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatTile label="Tenants activos" value="47" hint="3 en trial · 2 suspendidos" tone="primary" />
        <StatTile label="MRR consolidado" value="$ 38.4M" hint="+8.2% mes a mes" tone="success" />
        <StatTile label="Usuarios totales" value="412" tone="gold" />
        <StatTile label="Líneas WhatsApp" value="118" hint="Distribuidas en 47 tenants" />
      </div>

      <Card className="overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-[10px] uppercase tracking-wider text-muted-foreground font-bold">
            <tr>
              <th className="text-left px-4 py-3">Agencia</th>
              <th className="text-left px-4 py-3">Plan</th>
              <th className="text-right px-4 py-3">Usuarios</th>
              <th className="text-right px-4 py-3">Líneas</th>
              <th className="text-right px-4 py-3">MRR</th>
              <th className="text-left px-4 py-3">Estado</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {tenants.map((t) => (
              <tr key={t.name} className="hover:bg-muted/30 cursor-pointer">
                <td className="px-4 py-3 font-semibold">{t.name}</td>
                <td className="px-4 py-3"><span className="text-[10px] font-bold px-2 py-1 rounded-full bg-primary-soft text-primary">{t.plan}</span></td>
                <td className="px-4 py-3 text-right tabular-nums">{t.users}</td>
                <td className="px-4 py-3 text-right tabular-nums">{t.lines}</td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">{t.mrr}</td>
                <td className="px-4 py-3"><span className={`text-[10px] font-bold px-2 py-1 rounded-full uppercase ${badge[t.status]}`}>{t.status}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </ModulePage>
  );
}