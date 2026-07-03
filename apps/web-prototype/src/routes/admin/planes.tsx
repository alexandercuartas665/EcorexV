import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card } from "@/components/dashboard/ModulePage";
import { CreditCard, Check } from "lucide-react";

export const Route = createFileRoute("/admin/planes")({
  head: () => ({
    meta: [
      { title: "Planes · ECOREX Super Admin" },
      { name: "description", content: "Definición de planes comerciales, límites por tenant y precios." },
    ],
  }),
  component: Page,
});

const plans = [
  { name: "Starter", price: "$ 240K / mes", users: 3, lines: 1, ai: "5k llamadas IA", color: "border-border" },
  { name: "Pro", price: "$ 890K / mes", users: 12, lines: 5, ai: "50k llamadas IA", color: "border-primary/40 ring-1 ring-primary/30" },
  { name: "Enterprise", price: "$ 2.4M / mes", users: 50, lines: 20, ai: "500k llamadas IA", color: "border-gold/40" },
];

function Page() {
  return (
    <ModulePage
      icon={CreditCard}
      eyebrow="Super Admin SaaS"
      title="Planes comerciales"
      description="Cada plan define límites duros de usuarios, líneas WhatsApp, llamadas a la capa IA y automatizaciones."
    >
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {plans.map((p) => (
          <Card key={p.name} className={`p-6 ${p.color}`}>
            <div className="text-[10px] uppercase tracking-wider text-muted-foreground font-bold">{p.name}</div>
            <div className="text-3xl font-bold mt-1">{p.price}</div>
            <ul className="mt-5 space-y-2 text-sm">
              <li className="flex items-center gap-2"><Check className="size-4 text-success" /> {p.users} usuarios</li>
              <li className="flex items-center gap-2"><Check className="size-4 text-success" /> {p.lines} líneas WhatsApp</li>
              <li className="flex items-center gap-2"><Check className="size-4 text-success" /> {p.ai}</li>
              <li className="flex items-center gap-2"><Check className="size-4 text-success" /> Pipeline + Bandeja unificada</li>
              <li className="flex items-center gap-2"><Check className="size-4 text-success" /> Soporte multi-tenant</li>
            </ul>
            <button className="mt-6 w-full h-10 rounded-lg border border-border hover:bg-muted text-sm font-semibold">Editar plan</button>
          </Card>
        ))}
      </div>
    </ModulePage>
  );
}