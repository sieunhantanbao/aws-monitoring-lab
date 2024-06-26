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

var defaultResource = ResourceBuilder.CreateDefault().AddService("WorkerService");

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureLogging((hostBuilderContext,logging) =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddOpenTelemetry((options) =>
    {
        options.SetResourceBuilder(defaultResource);       
        options.AddOtlpExporter(otlOption =>
        {
            otlOption.Endpoint = new Uri("http://otel-collector:4317");
            otlOption.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        });
    });
});

builder.ConfigureServices((hostBuilderContext, services) =>
{
    //add code block to register opentelemetry for metrics and traces
    services.AddOpenTelemetry()
            .WithMetrics((providerBuilder) => providerBuilder
            .AddMeter("VotingMeter")
            .SetResourceBuilder(defaultResource)
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter()
            .AddOtlpExporter(otlOption =>
            {
                otlOption.Endpoint = new Uri("http://otel-collector:4317");
                otlOption.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            }));
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
