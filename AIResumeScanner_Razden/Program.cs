using AIResumeScanner_Razden.Components;
using AIResumeScanner_Razden.Services;
using AIResumeScanner_Razden.Models;
using Radzen;
using Azure;
using Azure.AI.TextAnalytics;

namespace AIResumeScanner_Razden
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddRazorPages();
            

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddRadzenComponents();

            // In Program.cs
            builder.Services.AddSingleton<TokenUsageService>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();

                return new TokenUsageService
                {
                    TokensPerMinuteLimit = config.GetValue<int>("AzureOpenAIModelDetails:TokensPerMinuteLimit", 10000),
                    MonthlyTokenLimit = config.GetValue<int>("AzureOpenAIModelDetails:MonthlyTokenLimit", 1000000)
                };
            });

            builder.Services.AddSingleton<ProfileValidationService>();
            // Register Azure Text Analytics Service
            //builder.Services.AddSingleton<ResumeValidationService>(sp =>
            //{
            //    // Get configuration from appsettings.json
            //    var configuration = sp.GetRequiredService<IConfiguration>();
            //    string endpoint = configuration["AzureTextAnalytics:Endpoint"];
            //    string apiKey = configuration["AzureTextAnalytics:ApiKey"];

            //    return new ResumeValidationService(endpoint, apiKey);
            //});

            builder.Services.AddHttpClient(); // Registers IHttpClientFactory
            builder.Services.AddScoped<AzureAISearchGrounding>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                string searchEndpoint = "https://resumeaisearchstore.search.windows.net";
                string searchApiKey = "4psgmJYJRx5OdFgCXrkcPJRhYsbH1t3hQIVhcML2MHAzSeCSOFvJ";
                string indexName = "dashboardsearchindex";
                string openAIEndpoint = "https://resumeembeddingendpoint.openai.azure.com/";
                string openAIApiKey = "BxUQYM8ND9UR2q3WqFrk2YlyHR4NHCG2ORy6xpublVSY4WIl3TwYJQQJ99BJACYeBjFXJ3w3AAABACOGeGiz";
                string embeddingDeployment = "text-embedding-ada-002";
                string chatDeployment = "gpt-5-nano";
                
                return new AzureAISearchGrounding(searchEndpoint, searchApiKey, indexName, openAIEndpoint, openAIApiKey, embeddingDeployment, chatDeployment);
            });
            builder.Services.AddSingleton<SignalRNotificationService>();
            builder.Services.AddSingleton<SentimentService>();
            //builder.Services.AddSingleton<SearchAgent>();
            //builder.Services.AddSingleton<AISearchPlugin>();
            //builder.Services.AddSingleton<ConversationStore>();
            builder.Services.AddSingleton(sp =>
                                                builder.Configuration.GetSection("ApiSettings").Get<ApiSettings>()
                                        );
            builder.Services.AddSignalR(
                                        options =>
                                        {
                                            options.EnableDetailedErrors = true;
                                            options.MaximumReceiveMessageSize = 1024 * 1024;
                                        });
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
          
            app.UseHttpsRedirection();

            app.UseStaticFiles();
            app.UseRouting();
            app.UseAntiforgery();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();


           
            app.MapRazorPages();
          



            app.Run();
        }
    }
}
