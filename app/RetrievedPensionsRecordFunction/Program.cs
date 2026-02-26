using MhpdCommon.Extensions;
using MhpdCommon.Models.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using RetrievedPensionsRecordFunction.Repository;
using System.Reflection;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);

builder.ConfigureFunctionsWebApplication();

if (!string.IsNullOrEmpty(builder.Configuration.GetValue<string>("ApplicationInsights:ConnectionString")))
{
    builder.Services.AddApplicationInsightsTelemetryWorkerService();
}

builder.Services.AddMhpdCosmosDb(builder.Configuration);
builder.Services.AddMhpdUtilities(builder.Configuration);
builder.Services.AddMhpdServiceBusTools();
builder.Services.AddTransformServices();
builder.Services.AddScoped<IPensionRecordRepository, PensionRecordRepository>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MaPS Retrieved Pension Records",
        Description = "This service allows a client to retrieve retrieved pension records related to a user session",
        Contact = new OpenApiContact
        {
            Name = "General Enquires",
            Email = "contact@maps.org.uk",
            Url = new Uri("https://maps.org.uk/en/about-us/contact-us")
        },
        License = new OpenApiLicense
        {
            Name = "Government API License",
            Url = new Uri("https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/")
        },
    });
    c.DocumentFilter<PensionDataOpenApiFilter>(Assembly.GetExecutingAssembly());
    c.DocumentFilter<OpenApiParameterFilter>(Assembly.GetExecutingAssembly());
    c.AddServer(new OpenApiServer
    {
        Url = builder.Configuration.GetValue<string>("OpenApiServerUrl") ?? "http://localhost:7289"
    });
});

builder
    .ConfigureAspNetCoreMvcIntegration(mvcBuilder =>
    {
        mvcBuilder.AddMvcOptions(mvcOptions => { });
    })
    .UseAspNetCoreMiddleware(app =>
    {
        app.UseFunctionSwaggerUI();

        app.UseSwagger(c => c.OpenApiVersion = OpenApiSpecVersion.OpenApi2_0);
        app.UseSwaggerUI();
    });

var app = builder.Build();
await app.RunAsync();
