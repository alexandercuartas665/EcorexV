import { createFileRoute } from "@tanstack/react-router";
import { ModulePage, Card, StatTile } from "@/components/dashboard/ModulePage";
import { Bot, Sparkles, Plus } from "lucide-react";

export const Route = createFileRoute("/agentes")({
  head: () => ({
    meta: [
      { title: "Agentes IA · ECOREX.tareas" },
      { name: "description", content: "Copilotos comerciales por agencia: clasificación de leads, extracción de datos y respuestas asistidas." },
    ],
  }),
  component: Page,
});

const agents = [
  { name: "Clasificador de Leads", purpose: "Etiqueta intención de viaje, destino y presupuesto.", mode: "Producción", calls: 1284, status: "active" },
  { name: "Asistente de Cotización", purpose: "Prepara borradores de cotización a partir del chat.", mode: "Producción", calls: 412, status: "active" },
  { name: "Detector de Riesgo", purpose: "Identifica chats que llevan +24h sin respuesta.", mode: "Pruebas", calls: 87, status: "test" },
];

function Page() {
  return (
    <ModulePage
      icon={Bot}
      eyebrow="Capa de Inteligencia"
      title="Agentes IA"
      description="Copilotos comerciales bajo políticas por tenant: presupuesto, escalamiento humano y trazabilidad de cada acción."
      actions={
        <button className="h-10 px-4 rounded-lg bg-foreground text-background text-sm font-semibold inline-flex items-center gap-2 shadow-soft">
          <Plus className="size-4" /> Nuevo agente
        </button>
      }
    >
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatTile label="Agentes activos" value="2" tone="primary" />
        <StatTile label="Llamadas IA hoy" value="1,783" hint="Plan: 50k / mes" tone="gold" />
        <StatTile label="Tasa de aceptación" value="78%" hint="Sugerencias usadas por asesores" tone="success" />
        <StatTile label="Escalamientos humanos" value="42" hint="Última semana" />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {agents.map((a) => (
          <Card key={a.name} className="p-5">
            <div className="flex items-start justify-between">
              <div className="size-9 rounded-lg bg-gold/15 grid place-items-center"><Sparkles className="size-4 text-gold-foreground" /></div>
              <span className={`text-[10px] font-bold px-2 py-1 rounded-full ${a.status === "active" ? "bg-success/15 text-success" : "bg-gold/15 text-gold-foreground"}`}>{a.mode}</span>
            </div>
            <h3 className="font-bold text-base mt-3">{a.name}</h3>
            <p className="text-xs text-muted-foreground mt-1">{a.purpose}</p>
            <div className="mt-4 pt-4 border-t border-border flex items-center justify-between text-xs">
              <span className="text-muted-foreground">{a.calls.toLocaleString()} llamadas</span>
              <button className="font-semibold text-primary hover:underline">Configurar</button>
            </div>
          </Card>
        ))}
      </div>
    </ModulePage>
  );
}