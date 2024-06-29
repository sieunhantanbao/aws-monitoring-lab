using System;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using worker;
using VotingData.Db;
using worker.Consumers;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using RabbitMQ.Client;
using OpenTelemetry.Trace;
using System.Diagnostics.Metrics;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;

var defaultResource = ResourceBuilder.CreateDefault();

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureLogging((hostBuilderContext,logging) =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddOpenTelemetry((options) =>
    {
        options.IncludeFormattedMessage = true;
        options.ParseStateValues = true;
        options.IncludeScopes = true;
        options.SetResourceBuilder(defaultResource);       
        options.AddOtlpExporter();
    });
});

builder.ConfigureServices((hostBuilderContext, services) =>
{
    services.AddOpenTelemetry()
            .WithMetrics(metrics => {
                var meter = new Meter("VotingMeter");
                metrics
                  .AddMeter(meter.Name)
                  .SetResourceBuilder(defaultResource)
                  .AddAspNetCoreInstrumentation()
                  .AddHttpClientInstrumentation();

                metrics
                  .AddConsoleExporter()
                  .AddOtlpExporter();
            })
           .WithTracing(traces =>
           {
              traces
                .SetResourceBuilder(defaultResource)
                .AddSource("Npgsql")
                .AddSource("MassTransit")
                .AddXRayTraceId()
                .AddAWSInstrumentation()
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                })
                .AddSqlClientInstrumentation(options => options.SetDbStatementForText = true)
                .AddMassTransitInstrumentation();

              traces
               .AddConsoleExporter()
               .AddOtlpExporter();
           });

    Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());

    var connectionString = hostBuilderContext.Configuration.GetConnectionString("SqlDbConnection");
    services.AddDbContext<VotingDBContext>(options =>options.UseNpgsql(connectionString));

    services.AddHostedService<Worker>();
    
    //RabittMQ over Masstransit
    services.AddMassTransit(x =>
    {
        x.AddConsumer<MessageConsumer>();
        x.UsingRabbitMq((context, cfg) =>
            {
                    cfg.Host(hostBuilderContext.Configuration.GetValue<string>("MassTransit:RabbitMq:Host"));
                    cfg.ConfigureEndpoints(context);
                });
    });
    
    //Add code block to register opentelemetr metric provider here

    //Add code block to register opentelemetr trace provider here
    
});

await builder.Build().RunAsync();
