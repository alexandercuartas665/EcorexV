import { createFileRoute, Link } from "@tanstack/react-router";
import {
  KanbanSquare, MessagesSquare, Smartphone, Bot, ArrowUpRight,
  Sparkles, Zap, Plus, FileText, MessageCircle, TrendingUp,
  Clock, AlertTriangle, CheckCircle2, Plane, DollarSign,
  Users, ChevronRight, Activity,
} from "lucide-react";

export const Route = createFileRoute("/")({
  head: () => ({
    meta: [
      { title: "ECOREX.tareas — Dashboard de tu Agencia" },
      { name: "description", content: "Pipeline, conversaciones WhatsApp, agentes IA y métricas comerciales de tu agencia turística en un solo lugar." },
    ],
  }),
  component: Dashboard,
});

const kpis = [
  { label: "Leads activos", value: "184", hint: "42 nuevos esta semana", icon: Users, tone: "primary", delta: "+18%" },
  { label: "Cotizaciones abiertas", value: "37", hint: "$ 248M en pipeline", icon: FileText, tone: "gold", delta: "+9%" },
  { label: "Cierres del mes", value: "23", hint: "Meta: 30 (76%)", icon: CheckCircle2, tone: "success", delta: "+24%" },
  { label: "WhatsApp sin responder", value: "7", hint: "2 vencidos · SLA 15min", icon: MessageCircle, tone: "danger", delta: "-3" },
];

const toneMap: Record<string, string> = {
  primary: "bg-primary-soft text-primary",
  gold: "bg-gold/15 text-gold-foreground",
  success: "bg-success/15 text-success",
  danger: "bg-destructive/10 text-destructive",
  muted: "bg-muted text-muted-foreground",
};

function Dashboard() {
  return (
    <div className="p-6 lg:p-8 max-w-[1600px] mx-auto space-y-6">
      {/* Greeting */}
      <section className="flex flex-col lg:flex-row lg:items-end lg:justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <span className="text-[11px] font-bold uppercase tracking-[0.18em] text-primary">Martes · 19 de Mayo, 2026</span>
            <span className="size-1 rounded-full bg-muted-foreground/40" />
            <span className="text-[11px] font-medium text-muted-foreground">Semana 21</span>
          </div>
          <h1 className="text-3xl font-bold tracking-tight mt-1">
            Buenos días, <span className="text-primary">Sofía</span>.
          </h1>
          <p className="text-sm text-muted-foreground mt-1 max-w-xl">
            Hoy tu agencia tiene <strong className="text-foreground">42 leads nuevos</strong>, <strong className="text-destructive">7 chats sin responder</strong> y 12 cotizaciones por seguir.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button className="h-10 px-4 rounded-lg border border-border bg-card hover:bg-muted text-sm font-semibold inline-flex items-center gap-2">
            <FileText className="size-4" /> Reporte semanal
          </button>
          <Link to="/pipeline" className="h-10 px-4 rounded-lg bg-foreground text-background hover:bg-foreground/90 text-sm font-semibold inline-flex items-center gap-2 shadow-soft">
            <Plus className="size-4" /> Nuevo lead
          </Link>
        </div>
      </section>

      {/* KPIs */}
      <section className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {kpis.map((k) => (
          <div key={k.label} className="rounded-2xl border border-border bg-card p-5 shadow-soft">
            <div className="flex items-start justify-between">
              <div className={`size-9 rounded-xl grid place-items-center ${toneMap[k.tone]}`}>
                <k.icon className="size-4.5" strokeWidth={2.2} />
              </div>
              <span className={`text-[10px] font-bold px-1.5 py-0.5 rounded-md ${k.delta.startsWith("+") ? "bg-success/15 text-success" : "bg-destructive/10 text-destructive"}`}>
                {k.delta}
              </span>
            </div>
            <div className="mt-4">
              <div className="text-[10px] uppercase tracking-wider text-muted-foreground font-bold">{k.label}</div>
              <div className="text-3xl font-bold tabular-nums mt-1">{k.value}</div>
              <div className="text-[11px] text-muted-foreground mt-0.5">{k.hint}</div>
            </div>
          </div>
        ))}
      </section>

      {/* Main grid */}
      <section className="grid grid-cols-12 gap-6">
        {/* Pipeline snapshot */}
        <div className="col-span-12 lg:col-span-8 rounded-2xl border border-border bg-card p-6 shadow-soft">
          <header className="flex items-start justify-between mb-6">
            <div>
              <div className="flex items-center gap-2">
                <h2 className="font-bold text-base tracking-tight">Pipeline comercial</h2>
                <span className="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-full bg-primary-soft text-primary">En vivo</span>
              </div>
              <p className="text-xs text-muted-foreground mt-1">5 etapas · 184 leads activos · $ 248M proyectado</p>
            </div>
            <Link to="/pipeline" className="text-xs font-semibold text-primary hover:underline inline-flex items-center gap-1">
              Abrir Kanban <ArrowUpRight className="size-3.5" />
            </Link>
          </header>

          <div className="grid grid-cols-5 gap-3">
            {[
              { stage: "Lead", count: 84, value: "$ 56M", color: "bg-primary/70" },
              { stage: "Cotización", count: 37, value: "$ 92M", color: "bg-primary" },
              { stage: "Inquietudes", count: 21, value: "$ 48M", color: "bg-gold" },
              { stage: "Cierre", count: 18, value: "$ 38M", color: "bg-success" },
              { stage: "Seguimiento", count: 24, value: "$ 14M", color: "bg-muted-foreground/60" },
            ].map((s) => (
              <div key={s.stage} className="rounded-xl border border-border bg-background/60 p-3">
                <div className="flex items-center gap-1.5">
                  <span className={`size-2 rounded-full ${s.color}`} />
                  <span className="text-[10px] font-bold uppercase tracking-wider text-muted-foreground">{s.stage}</span>
                </div>
                <div className="text-2xl font-bold mt-2 tabular-nums">{s.count}</div>
                <div className="text-[11px] text-muted-foreground mt-0.5">{s.value}</div>
              </div>
            ))}
          </div>

          <div className="mt-6 rounded-xl bg-muted/40 p-4 flex items-center gap-4">
            <div className="size-10 rounded-lg bg-success/15 grid place-items-center">
              <TrendingUp className="size-5 text-success" />
            </div>
            <div className="flex-1">
              <div className="text-sm font-semibold">Tasa de cierre: 12.4%</div>
              <div className="text-xs text-muted-foreground">+2.1 pts vs mes anterior · Mejor asesor: Camila Ruiz (18%)</div>
            </div>
            <Link to="/metricas" className="text-xs font-semibold text-primary hover:underline">Ver métricas</Link>
          </div>
        </div>

        {/* IA Copilot Panel */}
        <div className="col-span-12 lg:col-span-4 rounded-2xl border border-primary/20 bg-gradient-to-br from-primary to-primary/85 text-primary-foreground p-6 shadow-elevated relative overflow-hidden">
          <div className="absolute -top-12 -right-12 size-48 rounded-full bg-gold/20 blur-3xl" />
          <div className="absolute bottom-0 right-0 size-32 rounded-full bg-primary-foreground/5 blur-2xl" />
          <div className="relative">
            <div className="flex items-center gap-2 mb-1">
              <div className="size-7 rounded-lg bg-gold grid place-items-center">
                <Sparkles className="size-4 text-gold-foreground" />
              </div>
              <span className="text-[10px] font-bold uppercase tracking-[0.18em] text-primary-foreground/80">Copiloto IA · ECOREX</span>
            </div>
            <h3 className="font-bold text-lg tracking-tight mt-3">3 oportunidades detectadas hoy</h3>
            <p className="text-sm text-primary-foreground/80 mt-1">El agente analizó 247 conversaciones esta mañana.</p>

            <ul className="mt-5 space-y-3">
              <li className="rounded-xl bg-primary-foreground/10 backdrop-blur-sm border border-primary-foreground/15 p-3">
                <div className="flex items-start gap-2">
                  <Zap className="size-4 text-gold shrink-0 mt-0.5" />
                  <div>
                    <div className="text-[13px] font-semibold leading-snug">8 leads listos para cierre</div>
                    <div className="text-[11px] text-primary-foreground/70 mt-0.5">Intención de compra alta · paquetes Cartagena / San Andrés.</div>
                  </div>
                </div>
              </li>
              <li className="rounded-xl bg-primary-foreground/10 backdrop-blur-sm border border-primary-foreground/15 p-3">
                <div className="flex items-start gap-2">
                  <Zap className="size-4 text-gold shrink-0 mt-0.5" />
                  <div>
                    <div className="text-[13px] font-semibold leading-snug">14 chats con +24h sin respuesta</div>
                    <div className="text-[11px] text-primary-foreground/70 mt-0.5">Riesgo de pérdida · sugerido reasignar a turno noche.</div>
                  </div>
                </div>
              </li>
            </ul>
            <Link to="/agentes" className="mt-5 w-full h-10 rounded-lg bg-primary-foreground text-primary hover:bg-primary-foreground/95 text-sm font-bold inline-flex items-center justify-center gap-2">
              Abrir Copiloto <ArrowUpRight className="size-4" />
            </Link>
          </div>
        </div>

        {/* Tareas del día */}
        <div className="col-span-12 lg:col-span-5 rounded-2xl border border-border bg-card shadow-soft overflow-hidden">
          <header className="px-6 pt-5 pb-3 flex items-center justify-between">
            <div>
              <h2 className="font-bold text-base tracking-tight">Tu cola comercial</h2>
              <p className="text-xs text-muted-foreground mt-0.5">Asignados a ti · prioridad alta</p>
            </div>
            <span className="text-[10px] font-bold px-2 py-1 rounded-full bg-muted">9</span>
          </header>
          <ul className="divide-y divide-border">
            {[
              { icon: AlertTriangle, tone: "danger", title: "PQR — María Ortiz (Apto cancelado)", meta: "Vence en 2h · Lead #4821", time: "11:00" },
              { icon: MessageCircle, tone: "primary", title: "Llamada con Juan Pérez · cotización SMR", meta: "Cartagena · 5 pax · $ 8.2M", time: "14:00" },
              { icon: Plane, tone: "gold", title: "Enviar itinerario final — Familia Gómez", meta: "Cancún · sale 28 May", time: "16:00" },
              { icon: FileText, tone: "muted", title: "Confirmar pago Wompi #PG-2918", meta: "$ 4.5M · cuenta corporativa", time: "Hoy" },
              { icon: CheckCircle2, tone: "success", title: "Cerrar caso #408 — Luna de miel Bali", meta: "Pagado · documentación enviada", time: "Hecho" },
            ].map((t, i) => (
              <li key={i} className="flex items-start gap-3 px-6 py-3.5 hover:bg-muted/40 transition-colors group cursor-pointer">
                <div className={`size-8 rounded-lg grid place-items-center shrink-0 ${toneMap[t.tone]}`}>
                  <t.icon className="size-4" strokeWidth={2.2} />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="text-sm font-semibold text-foreground truncate">{t.title}</div>
                  <div className="text-xs text-muted-foreground mt-0.5">{t.meta}</div>
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  <Clock className="size-3 text-muted-foreground" />
                  <span className="text-[11px] text-muted-foreground tabular-nums font-medium">{t.time}</span>
                </div>
              </li>
            ))}
          </ul>
        </div>

        {/* WhatsApp lines health */}
        <div className="col-span-12 lg:col-span-3 rounded-2xl border border-border bg-card p-5 shadow-soft flex flex-col">
          <header className="flex items-center justify-between mb-4">
            <div className="flex items-center gap-2">
              <div className="size-7 rounded-lg bg-success/15 grid place-items-center">
                <Smartphone className="size-4 text-success" />
              </div>
              <h3 className="font-bold text-sm">Líneas WhatsApp</h3>
            </div>
            <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-success/15 text-success">3/3 activas</span>
          </header>
          <ul className="space-y-3 flex-1">
            {[
              { name: "Ventas — Vuelos", num: "+57 320 ··· 4521", state: "online", count: 142 },
              { name: "Paquetes vacacionales", num: "+57 311 ··· 8809", state: "online", count: 87 },
              { name: "Servicio post-venta", num: "+57 304 ··· 2244", state: "warn", count: 23 },
            ].map((l) => (
              <li key={l.num} className="flex items-start gap-2.5">
                <div className={`size-2 rounded-full mt-1.5 ${l.state === "online" ? "bg-success animate-pulse" : "bg-gold"}`} />
                <div className="flex-1 min-w-0">
                  <div className="text-xs font-semibold truncate">{l.name}</div>
                  <div className="text-[11px] text-muted-foreground truncate mt-0.5">{l.num} · {l.count} chats</div>
                </div>
              </li>
            ))}
          </ul>
          <Link to="/lineas" className="mt-3 text-[11px] font-semibold text-primary hover:underline inline-flex items-center gap-1 self-start">
            Gestionar líneas <ChevronRight className="size-3" />
          </Link>
        </div>

        {/* Quick access modules */}
        <div className="col-span-12 rounded-2xl border border-border bg-card p-6 shadow-soft">
          <header className="flex items-center justify-between mb-5">
            <div>
              <h2 className="font-bold text-base tracking-tight">Módulos del SaaS</h2>
              <p className="text-xs text-muted-foreground mt-0.5">Acceso rápido a toda la operación comercial</p>
            </div>
            <span className="text-[10px] font-bold px-2 py-1 rounded-full bg-primary-soft text-primary">Plan Pro</span>
          </header>
          <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3">
            {[
              { icon: KanbanSquare, title: "Pipeline", meta: "5 etapas", url: "/pipeline", tone: "primary" },
              { icon: MessagesSquare, title: "Conversaciones", meta: "7 sin leer", url: "/conversaciones", tone: "danger" },
              { icon: Users, title: "Leads", meta: "184 activos", url: "/leads", tone: "primary" },
              { icon: Users, title: "Asesores", meta: "12 conectados", url: "/asesores", tone: "primary" },
              { icon: Smartphone, title: "Líneas WhatsApp", meta: "3 activas", url: "/lineas", tone: "success" },
              { icon: Bot, title: "Agentes IA", meta: "2 en producción", url: "/agentes", tone: "gold" },
              { icon: Zap, title: "Automatizaciones", meta: "8 reglas", url: "/automatizaciones", tone: "gold" },
              { icon: Activity, title: "Métricas", meta: "Tasa cierre 12.4%", url: "/metricas", tone: "primary" },
              { icon: DollarSign, title: "Facturación", meta: "Wompi conectado", url: "/admin/facturacion", tone: "success" },
              { icon: Sparkles, title: "Copiloto IA", meta: "3 oportunidades", url: "/agentes", tone: "gold" },
            ].map((m) => (
              <Link key={m.title} to={m.url} className="group text-left rounded-xl border border-border p-3.5 hover:border-primary/40 hover:bg-primary-soft/30 transition-all">
                <div className={`size-9 rounded-lg grid place-items-center mb-3 ${toneMap[m.tone]}`}>
                  <m.icon className="size-4.5" strokeWidth={2.2} />
                </div>
                <div className="text-sm font-bold tracking-tight">{m.title}</div>
                <div className="text-[11px] text-muted-foreground mt-0.5">{m.meta}</div>
              </Link>
            ))}
          </div>
        </div>
      </section>

      <footer className="text-center text-[11px] text-muted-foreground pt-2 pb-4">
        ECOREX.tareas v0.1 · CRM Conversacional Multi-Tenant para Agencias Turísticas
      </footer>
    </div>
  );
}