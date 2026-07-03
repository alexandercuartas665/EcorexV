import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card } from "@/components/dashboard/ModulePage";
import { ScrollText } from "lucide-react";

export const Route = createFileRoute("/admin/auditoria")({
  head: () => ({
    meta: [
      { title: "Auditoría · ECOREX Super Admin" },
      { name: "description", content: "Trazabilidad de acciones críticas por tenant: cambios de plan, suspensiones, accesos y errores." },
    ],
  }),
  component: Page,
});

const logs = [
  { ts: "19 May 09:42", actor: "super.admin", tenant: "Pacífico Sun", action: "Suspensión por mora", level: "warn" },
  { ts: "19 May 09:01", actor: "sistema", tenant: "Caribe Vuela", action: "Trial iniciado (14 días)", level: "info" },
  { ts: "18 May 18:22", actor: "super.admin", tenant: "MarSol Tours", action: "Upgrade Starter → Pro", level: "info" },
  { ts: "18 May 14:10", actor: "sistema", tenant: "Andes Travel", action: "Reconexión línea +57 304···2244", level: "warn" },
  { ts: "18 May 10:05", actor: "wompi.webhook", tenant: "Bogotá Travel Group", action: "Pago confirmado $ 2.4M", level: "ok" },
];

const tone: Record<string, string> = {
  info: "bg-primary-soft text-primary",
  warn: "bg-gold/15 text-gold-foreground",
  ok: "bg-success/15 text-success",
  err: "bg-destructive/10 text-destructive",
};

function Page() {
  return (
    <ModulePage
      icon={ScrollText}
      eyebrow="Super Admin SaaS"
      title="Auditoría global"
      description="Cada acción crítica del SaaS queda registrada con tenant, actor, timestamp y payload para cumplimiento."
    >
      <Card className="overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-[10px] uppercase tracking-wider text-muted-foreground font-bold">
            <tr>
              <th className="text-left px-4 py-3">Timestamp</th>
              <th className="text-left px-4 py-3">Actor</th>
              <th className="text-left px-4 py-3">Tenant</th>
              <th className="text-left px-4 py-3">Acción</th>
              <th className="text-left px-4 py-3">Nivel</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {logs.map((l, i) => (
              <tr key={i} className="hover:bg-muted/30">
                <td className="px-4 py-3 text-xs text-muted-foreground font-mono">{l.ts}</td>
                <td className="px-4 py-3 text-xs font-mono">{l.actor}</td>
                <td className="px-4 py-3 font-semibold">{l.tenant}</td>
                <td className="px-4 py-3 text-sm">{l.action}</td>
                <td className="px-4 py-3"><span className={`text-[10px] font-bold px-2 py-1 rounded-full uppercase ${tone[l.level]}`}>{l.level}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </ModulePage>
  );
}