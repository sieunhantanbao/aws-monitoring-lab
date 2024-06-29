// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using OpenTelemetry;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.Diagnostics.Metrics;
using VotingData.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var defaultResource = ResourceBuilder.CreateDefault();

builder.Logging.AddOpenTelemetry(options =>
{
      options.IncludeFormattedMessage = true;
      options.ParseStateValues = true;
      options.IncludeScopes = true;
      options.SetResourceBuilder(defaultResource);
      options.AddOtlpExporter(opts =>
      {
            opts.Endpoint = new Uri("http://otel-collector:4317");
            opts.ExportProcessorType = ExportProcessorType.Batch;
            opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
      });    
});

builder.Services.AddOpenTelemetry()
      .WithMetrics(metrics => {
          var meter = new Meter("VotingMeter");
          metrics
            .AddMeter(meter.Name)
            .SetResourceBuilder(defaultResource)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

          metrics
            .AddConsoleExporter()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("http://otel-collector:4317");
                options.ExportProcessorType = ExportProcessorType.Batch;
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            });
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
           .AddOtlpExporter(options =>
           {
               options.Endpoint = new Uri("http://otel-collector:4317");
               options.ExportProcessorType = ExportProcessorType.Batch;
               options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
           });
      });

 Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());

builder.Services.AddCors(options =>
            {
                options.AddPolicy("No-Restrict-Policy",
                                                policy =>
                                                {
                                                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                                                });
            });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "votingdata", Version = "v1" });

});

var connectionString = builder.Configuration.GetConnectionString("SqlDbConnection");
builder.Services.AddDbContext<VotingDBContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetValue<string>("MassTransit:RabbitMq:Host"));
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});



if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseCors("No-Restrict-Policy");
//app.UseHttpsRedirection();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "app v1");
    options.RoutePrefix = string.Empty;
});

app.UseRouting();
app.UseEndpoints(builder =>
{
    builder.MapControllers();
});

app.Run();
