using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResumeMetadataExtractFunc.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Application Insights (Functions way)
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Logging
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddConsole();
            loggingBuilder.AddApplicationInsights(
                configureTelemetryConfiguration: _ => { },
                configureApplicationInsightsLoggerOptions: options =>
                {
                    options.TrackExceptionsAsExceptionTelemetry = true;
                });
        });

        // Your services
        services.AddScoped<ChunkingService>();
        services.AddScoped<EmbeddingService>();
    })
    .Build();

host.Run();
