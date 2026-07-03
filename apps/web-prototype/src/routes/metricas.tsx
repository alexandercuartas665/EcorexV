import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card, StatTile } from "@/components/dashboard/ModulePage";
import { BarChart3 } from "lucide-react";

export const Route = createFileRoute("/metricas")({
  head: () => ({
    meta: [
      { title: "Métricas · ECOREX.tareas" },
      { name: "description", content: "Analítica comercial: conversión por etapa, performance de asesores y volumen conversacional." },
    ],
  }),
  component: Page,
});

function Page() {
  return (
    <ModulePage
      icon={BarChart3}
      eyebrow="Analítica"
      title="Métricas comerciales"
      description="Conversión por etapa, ranking de asesores, volumen por línea WhatsApp y proyecciones IA."
    >
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatTile label="Tasa de cierre" value="12.4%" hint="+2.1 pts vs mes anterior" tone="success" />
        <StatTile label="Tiempo promedio etapa" value="4.2 días" hint="Lead → Cierre" tone="primary" />
        <StatTile label="Ingresos cerrados" value="$ 184M" hint="Mes en curso" tone="gold" />
        <StatTile label="Leads perdidos" value="18" hint="9% del total" tone="danger" />
      </div>

      <Card className="p-8 grid place-items-center text-center min-h-[280px]">
        <div>
          <BarChart3 className="size-10 text-muted-foreground/40 mx-auto mb-3" />
          <h3 className="font-bold text-base">Gráficas conectadas a datos reales</h3>
          <p className="text-sm text-muted-foreground mt-1 max-w-md">Disponibles al activar Lovable Cloud y conectar el backend. Hoy se muestran indicadores con datos de demo.</p>
        </div>
      </Card>
    </ModulePage>
  );
}