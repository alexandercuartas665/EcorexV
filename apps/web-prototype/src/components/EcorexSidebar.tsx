import { Link, useRouterState } from "@tanstack/react-router";
import {
  LayoutDashboard, KanbanSquare, MessagesSquare, UserSquare2,
  Users, Smartphone, Bot, Zap, BarChart3,
  Building2, CreditCard, Receipt, ScrollText,
  ChevronsUpDown, Search, Plane,
} from "lucide-react";
import {
  Sidebar, SidebarContent, SidebarGroup, SidebarGroupContent,
  SidebarGroupLabel, SidebarMenu, SidebarMenuButton, SidebarMenuItem,
  SidebarHeader, SidebarFooter, useSidebar,
} from "@/components/ui/sidebar";

const operacion = [
  { title: "Dashboard", url: "/", icon: LayoutDashboard, badge: null },
  { title: "Pipeline", url: "/pipeline", icon: KanbanSquare, badge: "42" },
  { title: "Conversaciones", url: "/conversaciones", icon: MessagesSquare, badge: "7" },
  { title: "Leads", url: "/leads", icon: UserSquare2, badge: null },
  { title: "Asesores", url: "/asesores", icon: Users, badge: null },
];

const infraestructura = [
  { title: "Líneas WhatsApp", url: "/lineas", icon: Smartphone, badge: "3" },
  { title: "Agentes IA", url: "/agentes", icon: Bot, badge: null },
  { title: "Automatizaciones", url: "/automatizaciones", icon: Zap, badge: null },
  { title: "Métricas", url: "/metricas", icon: BarChart3, badge: null },
];

const superAdmin = [
  { title: "Tenants", url: "/admin/tenants", icon: Building2 },
  { title: "Planes", url: "/admin/planes", icon: CreditCard },
  { title: "Facturación", url: "/admin/facturacion", icon: Receipt },
  { title: "Auditoría", url: "/admin/auditoria", icon: ScrollText },
];

export function EcorexSidebar() {
  const { state } = useSidebar();
  const collapsed = state === "collapsed";
  const path = useRouterState({ select: (r) => r.location.pathname });
  const isActive = (url: string) => path === url;

  return (
    <Sidebar collapsible="icon" className="border-r border-sidebar-border">
      <SidebarHeader className="border-b border-sidebar-border px-3 py-4">
        <div className="flex items-center gap-2.5">
          <div className="relative size-9 shrink-0 rounded-xl bg-gradient-to-br from-primary to-primary/70 grid place-items-center shadow-soft">
            <Plane className="size-4 text-primary-foreground -rotate-45" strokeWidth={2.4} />
            <div className="absolute -bottom-0.5 -right-0.5 size-3 rounded-full bg-gold ring-2 ring-sidebar" />
          </div>
          {!collapsed && (
            <div className="flex flex-col leading-none">
              <span className="font-bold text-[15px] tracking-tight text-sidebar-foreground">ECOREX<span className="text-primary">.travels</span></span>
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground font-medium">CRM Conversacional</span>
            </div>
          )}
        </div>

        {!collapsed && (
          <button className="mt-4 w-full rounded-lg bg-primary-soft border border-primary/15 p-2.5 text-left hover:bg-primary-soft/70 transition-colors group">
            <div className="flex items-center justify-between">
              <div className="min-w-0">
                <div className="text-[10px] font-bold uppercase tracking-wider text-primary/80">Agencia</div>
                <div className="text-sm font-semibold text-foreground truncate">Andes Travel</div>
                <div className="text-[11px] text-muted-foreground">Plan Pro · 12 asesores</div>
              </div>
              <ChevronsUpDown className="size-4 text-primary/60 group-hover:text-primary shrink-0" />
            </div>
          </button>
        )}

        {!collapsed && (
          <div className="mt-3 relative">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 size-3.5 text-muted-foreground" />
            <input
              placeholder="Buscar lead, chat… ⌘K"
              className="w-full h-8 rounded-md bg-muted border border-border pl-8 pr-2 text-xs text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring/40"
            />
          </div>
        )}
      </SidebarHeader>

      <SidebarContent className="px-1">
        <SidebarGroup>
          <SidebarGroupLabel className="text-[10px] tracking-[0.14em] font-bold text-muted-foreground/80">
            Operación Comercial
          </SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {operacion.map((item) => (
                <SidebarMenuItem key={item.url}>
                  <SidebarMenuButton asChild isActive={isActive(item.url)} tooltip={item.title}>
                    <Link to={item.url} className="group/item">
                      <item.icon className="size-4" />
                      {!collapsed && <span className="flex-1 truncate">{item.title}</span>}
                      {!collapsed && item.badge && (
                        <span className="ml-auto text-[10px] font-semibold px-1.5 py-0.5 rounded-md bg-primary/10 text-primary">
                          {item.badge}
                        </span>
                      )}
                    </Link>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>

        <SidebarGroup>
          <SidebarGroupLabel className="text-[10px] tracking-[0.14em] font-bold text-muted-foreground/80">
            Infraestructura & IA
          </SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {infraestructura.map((item) => (
                <SidebarMenuItem key={item.url}>
                  <SidebarMenuButton asChild isActive={isActive(item.url)} tooltip={item.title}>
                    <Link to={item.url}>
                      <item.icon className="size-4" />
                      {!collapsed && <span className="flex-1 truncate">{item.title}</span>}
                      {!collapsed && item.badge && (
                        <span className="ml-auto text-[10px] font-semibold px-1.5 py-0.5 rounded-md bg-success/15 text-success">
                          {item.badge}
                        </span>
                      )}
                    </Link>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>

        <SidebarGroup>
          <SidebarGroupLabel className="text-[10px] tracking-[0.14em] font-bold text-muted-foreground/80 flex items-center gap-1.5">
            Super Admin SaaS
            <span className="size-1.5 rounded-full bg-gold" />
          </SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {superAdmin.map((item) => (
                <SidebarMenuItem key={item.url}>
                  <SidebarMenuButton asChild isActive={isActive(item.url)} tooltip={item.title}>
                    <Link to={item.url}>
                      <item.icon className="size-4" />
                      {!collapsed && <span className="truncate">{item.title}</span>}
                    </Link>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>

      <SidebarFooter className="border-t border-sidebar-border p-3">
        {!collapsed ? (
          <div className="flex items-center gap-2.5">
            <div className="size-9 rounded-full bg-gradient-to-br from-gold/80 to-gold grid place-items-center text-gold-foreground font-bold text-xs shrink-0">
              SM
            </div>
            <div className="min-w-0 flex-1">
              <div className="text-xs font-semibold truncate">Sofía Mejía</div>
              <div className="text-[11px] text-muted-foreground truncate">Admin · Andes Travel</div>
            </div>
            <div className="size-2 rounded-full bg-success animate-pulse" />
          </div>
        ) : (
          <div className="size-9 mx-auto rounded-full bg-gradient-to-br from-gold/80 to-gold grid place-items-center text-gold-foreground font-bold text-xs">
            SM
          </div>
        )}
      </SidebarFooter>
    </Sidebar>
  );
}