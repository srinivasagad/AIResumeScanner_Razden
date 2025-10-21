using Microsoft.AspNetCore.SignalR.Client;
using Radzen;

namespace AIResumeScanner_Razden.Models
{
    public class SignalRNotificationService
    {
        private HubConnection _connection;
        public HubConnectionState ConnectionState => _connection.State;
        //public event Action<SignalRNotificationMessage> OnNotificationReceived;
        public event Func<SignalRNotificationMessage, Task>? OnNotificationReceived;

        public async Task StartAsync(string hubUrl, string accessToken)
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(accessToken);
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<SignalRNotificationMessage>("ReceiveNotification", async (notification) =>
            {
                if (OnNotificationReceived != null)
                    await OnNotificationReceived?.Invoke(notification);                
            });

            await _connection.StartAsync();
        }

        public async Task StopAsync()
        {
            if (_connection != null)
                await _connection.StopAsync();
        }
    }

}
