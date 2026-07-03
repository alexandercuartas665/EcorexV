using Ecorex.Application;
using Ecorex.Application.Common;
using Ecorex.Infrastructure;
using Ecorex.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddScoped<ITenantContext, SystemTenantContext>();

builder.Services.AddHostedService<RecurringBillingWorker>();

var host = builder.Build();
host.Run();
