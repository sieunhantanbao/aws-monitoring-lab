// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using VotingData.Models;
using MassTransit;
using OpenTelemetry.Resources;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using System.Reflection;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();

var defaultResource = ResourceBuilder.CreateDefault().AddService("VotingApi");

builder.Logging.AddOpenTelemetry(options =>
{
  options.IncludeFormattedMessage = true;
  options.ParseStateValues = true;
  options.IncludeScopes = true;
  options.SetResourceBuilder(defaultResource);
  options.AddOtlpExporter(otlOption =>
  {
      otlOption.Endpoint = new Uri("http://localhost:4318");
      otlOption.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
  });        
});

builder.Services.AddOpenTelemetry()
    .WithMetrics((providerBuilder) => providerBuilder
      .AddMeter("VotingMeter")
      .SetResourceBuilder(defaultResource)
      .AddAspNetCoreInstrumentation()
      .AddConsoleExporter()
      .AddOtlpExporter(otlOption =>
      {
          otlOption.Endpoint = new Uri("http://localhost:4318");
          otlOption.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
      })
    );
//Add code block to register OpenTelemetry TraceProvider here

builder.Services.AddCors(options =>
            {
                options.AddPolicy("No-Restrict-Policy",
                                                policy =>
                                                {
                                                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                                                });
            });

builder.Services.AddControllers();

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
