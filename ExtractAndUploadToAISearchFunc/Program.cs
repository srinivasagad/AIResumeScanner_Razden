using ExtractAndUploadToAISearchFunc.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);


builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Ensure logger sends output to both Console (for Log Stream) and Application Insights
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConsole();
    loggingBuilder.AddApplicationInsights(
        configureTelemetryConfiguration: (config) => { },
        configureApplicationInsightsLoggerOptions: (options) =>
        {
            options.IncludeScopes = true;
            options.TrackExceptionsAsExceptionTelemetry = true;
        });
});

builder.Services.AddScoped<ChunkingService>();
builder.Services.AddScoped<EmbeddingService>();

builder.Build().Run();
