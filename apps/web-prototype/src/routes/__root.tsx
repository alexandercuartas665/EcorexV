import { Outlet, Link, createRootRoute, HeadContent, Scripts } from "@tanstack/react-router";
import { SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { EcorexSidebar } from "@/components/EcorexSidebar";
import { Bell, HelpCircle, Sparkles } from "lucide-react";
import appCss from "../styles.css?url";

function NotFoundComponent() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="max-w-md text-center">
        <h1 className="text-7xl font-bold text-foreground">404</h1>
        <h2 className="mt-4 text-xl font-semibold">Página no encontrada</h2>
        <p className="mt-2 text-sm text-muted-foreground">Este módulo de ECOREX.tareas aún no está disponible.</p>
        <div className="mt-6">
          <Link to="/" className="inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90">
            Ir al Dashboard
          </Link>
        </div>
      </div>
    </div>
  );
}

export const Route = createRootRoute({
  head: () => ({
    meta: [
      { charSet: "utf-8" },
      { name: "viewport", content: "width=device-width, initial-scale=1" },
      { title: "ECOREX.crm" },
      { name: "description", content: "Plataforma SaaS multi-tenant para agencias de viajes: pipeline comercial, WhatsApp con Evolution API y agentes de IA." },
      { property: "og:title", content: "ECOREX.crm" },
      { name: "twitter:title", content: "ECOREX.crm" },
      { property: "og:description", content: "Plataforma SaaS multi-tenant para agencias de viajes: pipeline comercial, WhatsApp con Evolution API y agentes de IA." },
      { name: "twitter:description", content: "Plataforma SaaS multi-tenant para agencias de viajes: pipeline comercial, WhatsApp con Evolution API y agentes de IA." },
      { property: "og:image", content: "https://pub-bb2e103a32db4e198524a2e9ed8f35b4.r2.dev/3182ba8c-b554-4cf8-9a7a-43632bb22075/id-preview-b787e7e6--ae1b5170-56e9-4504-afc8-4fce78c4078a.lovable.app-1779202875727.png" },
      { name: "twitter:image", content: "https://pub-bb2e103a32db4e198524a2e9ed8f35b4.r2.dev/3182ba8c-b554-4cf8-9a7a-43632bb22075/id-preview-b787e7e6--ae1b5170-56e9-4504-afc8-4fce78c4078a.lovable.app-1779202875727.png" },
      { name: "twitter:card", content: "summary_large_image" },
      { property: "og:type", content: "website" },
    ],
    links: [
      { rel: "stylesheet", href: appCss },
      { rel: "preconnect", href: "https://fonts.googleapis.com" },
      { rel: "preconnect", href: "https://fonts.gstatic.com", crossOrigin: "anonymous" },
      { rel: "stylesheet", href: "https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800&display=swap" },
    ],
  }),
  shellComponent: RootShell,
  component: RootComponent,
  notFoundComponent: NotFoundComponent,
});

function RootShell({ children }: { children: React.ReactNode }) {
  return (
    <html lang="es">
      <head>
        <HeadContent />
      </head>
      <body>
        {children}
        <Scripts />
      </body>
    </html>
  );
}

function RootComponent() {
  return (
    <SidebarProvider>
      <div className="min-h-screen flex w-full bg-background">
        <EcorexSidebar />
        <div className="flex-1 flex flex-col min-w-0">
          <header className="h-14 border-b border-border bg-card/60 backdrop-blur-sm flex items-center justify-between px-4 sticky top-0 z-30">
            <div className="flex items-center gap-3">
              <SidebarTrigger />
              <div className="h-5 w-px bg-border" />
              <nav className="flex items-center gap-2 text-sm">
                <span className="text-muted-foreground">Andes Travel</span>
                <span className="text-muted-foreground/40">/</span>
                <span className="font-semibold text-foreground">Dashboard</span>
              </nav>
            </div>
            <div className="flex items-center gap-2">
              <button className="hidden md:inline-flex items-center gap-2 h-9 px-3 rounded-lg border border-border bg-background hover:bg-muted text-xs font-medium">
                <Sparkles className="size-3.5 text-gold" />
                Copiloto IA
                <kbd className="ml-1 px-1.5 py-0.5 rounded bg-muted text-[10px] text-muted-foreground border border-border">⌘K</kbd>
              </button>
              <button className="size-9 rounded-lg border border-border bg-background hover:bg-muted grid place-items-center relative">
                <Bell className="size-4" />
                <span className="absolute top-1.5 right-1.5 size-2 rounded-full bg-destructive ring-2 ring-card" />
              </button>
              <button className="size-9 rounded-lg border border-border bg-background hover:bg-muted grid place-items-center">
                <HelpCircle className="size-4" />
              </button>
            </div>
          </header>
          <main className="flex-1 min-w-0">
            <Outlet />
          </main>
        </div>
      </div>
    </SidebarProvider>
  );
}
