using AIResumeScanner_Razden.Components;
using AIResumeScanner_Razden.Services;
using AIResumeScanner_Razden.Models;
using Radzen;

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

            builder.Services.AddHttpClient(); // Registers IHttpClientFactory

            builder.Services.AddSingleton<SignalRNotificationService>();
            builder.Services.AddSingleton<SentimentService>();
            //builder.Services.AddSingleton<SearchAgent>();
            //builder.Services.AddSingleton<AISearchPlugin>();
            //builder.Services.AddSingleton<ConversationStore>();
            builder.Services.AddSingleton(sp =>
                                                builder.Configuration.GetSection("ApiSettings").Get<ApiSettings>());
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
