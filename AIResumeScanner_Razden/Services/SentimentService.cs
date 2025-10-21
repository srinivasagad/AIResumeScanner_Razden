using AIResumeScanner_Razden.Models;
using Microsoft.AspNetCore.Components.Forms;
using System.Net.Http.Headers;

namespace AIResumeScanner_Razden.Services
{
    public class SentimentService
    {

        private readonly HttpClient _httpClient;
        private readonly ApiSettings _settings;

        public SentimentService(IHttpClientFactory httpClientFactory, ApiSettings settings)
        {
            _httpClient = httpClientFactory.CreateClient();
            _settings = settings;
        }


        public async Task<string> AnalyzeSentimentAsync(JobModel jobModel, CancellationToken cancellationToken = default)
        {
            var apiUrl = _settings.SentimentEndpoint;

            // Prepare request content

            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(jobModel.JobDescription),
                System.Text.Encoding.UTF8,
                "application/json");

            // Create a CancellationTokenSource with a 5-minute timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(Convert.ToInt32(_settings.Timeout)));

            try
            {
                var response = await _httpClient.PostAsync(apiUrl, content, timeoutCts.Token);

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync();
                return result;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Sentiment analysis request timed out.");
            }
            catch (HttpRequestException ex)
            {
                throw new Exception("Error connecting to sentiment API: " + ex.Message, ex);
            }
        }

        public async Task<string> UploadFilesAsync(IEnumerable<IBrowserFile> files, CancellationToken cancellationToken = default)
        {
            var apiUrl = _settings.FileUploadEndpoint;

            // Create a CancellationTokenSource with a 5-minute timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(Convert.ToInt32(_settings.Timeout)));

            using var form = new MultipartFormDataContent();



            foreach (var file in files)
            {
                if (file == null) continue;
                if (string.IsNullOrWhiteSpace(file.Name)) continue;
                if (string.IsNullOrWhiteSpace(file.ContentType)) continue;
                if (file.Size <= 0) continue;

                // Read the file into a stream (limit max size if needed)
                using var stream = file.OpenReadStream(); // or specify a max size
                var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;

                var streamContent = new StreamContent(memoryStream);
                var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

                form.Add(streamContent, "files", file.Name);

               
            }



            var response = await _httpClient.PostAsync(apiUrl, form, timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsStringAsync();
            return result;

        }
    }
}
