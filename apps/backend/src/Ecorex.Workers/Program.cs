using Ecorex.Application;
using Ecorex.Application.Common;
using Ecorex.Infrastructure;
using Ecorex.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddScoped<ITenantContext, SystemTenantContext>();

builder.Services.AddHostedService<RecurringBillingWorker>();
// Limpieza diaria del TTL del historial de reglas (FASE 4 ola 3, ADR-0016).
builder.Services.AddHostedService<RuleLogTtlCleanupWorker>();

var host = builder.Build();
host.Run();
