using Azure.Search.Documents.Models;
using Azure.Search.Documents;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using ResumeParserWebApi.Models;
using System.Text.Json.Serialization;
using iText.IO.Util;
using Azure.Search.Documents.Indexes;

namespace ResumeParserWebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UpdateExistingAIIndexController : ControllerBase
    {
        private readonly ILogger<FileUploaderController> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
       
        public UpdateExistingAIIndexController(ILogger<FileUploaderController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = new HttpClient();
        }

      

        [HttpGet("search")]
        public async Task<IActionResult> SearchDocuments()
        {
            try
            {
                var azureAISearch = _configuration.GetSection("AISearch");
                var apiKey = azureAISearch["SearchApiKey"];
                var serviceEndpoint = azureAISearch["ServiceEndpoint"];
                var indexName = azureAISearch["SearchIndexName"];

                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(serviceEndpoint) || string.IsNullOrEmpty(indexName))
                    return BadRequest("Missing configuration.");

                var credential = new AzureKeyCredential(apiKey);
                var searchClient = new SearchClient(new Uri(serviceEndpoint), indexName, credential);

                PrintIndexSchema(indexName);

                var searchOptions = new SearchOptions
                {
                    Size = 1, // Number of results per page
                    Skip = 0,  // For pagination
                    IncludeTotalCount = true,
                    Select = { "Id","skills", "fileName", "metadata" },
                    Filter = "Id eq '2fc976ae-bd12-496f-abd5-9fd2bf20c020'"
                };

                SearchResults<SearchDocument> results = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);

                var documents = new List<SearchDocument>();
                await foreach (SearchResult<SearchDocument> result in results.GetResultsAsync())
                {
                    documents.Add(result.Document);

                    // Access fields
                    var id = result.Document.ContainsKey("Id") ? result.Document["Id"] : null;
                    var fileName = result.Document.ContainsKey("fileName") ? result.Document["fileName"] : null;
                    var metadata = result.Document.ContainsKey("metadata") ? result.Document["metadata"] : null;

                    Console.WriteLine($"ID: {id}, FileName: {fileName} , Metadata Url :{metadata}");

                    if (metadata != null && id != null )
                    {
                       
                        if (metadata != null && metadata.ToString().Length>0)
                        {
                            // Download the JSON content
                            string jsonContent = await _httpClient.GetStringAsync(Convert.ToString(metadata));

                            // Clean the JSON (remove escape characters)
                            string cleanedJson = CleanJson(jsonContent);

                            // Deserialize with options
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                            };

                            Resume resume = JsonSerializer.Deserialize<Resume>(cleanedJson, options);

                            // Now read the id and update if needed
                            id = "2fc976ae-bd12-496f-abd5-9fd2bf20c020";
                            var response = await searchClient.GetDocumentAsync<SearchDocument>(id.ToString());
                            var document = response.Value;
                            //document["experienceYears"] = resume.TotalExperienceYears;
                            document["skills"] = resume.Skills;
                            var batch = new IndexDocumentsBatch<SearchDocument>();
                            batch.Actions.Add(IndexDocumentsAction.MergeOrUpload(document));
                            await searchClient.IndexDocumentsAsync(batch);

                        }
                    }
                }

                Console.WriteLine($"Total documents: {results.TotalCount}");

                return Ok(new
                {
                    TotalCount = results.TotalCount,
                    Page = 1,
                    PageSize = 1,
                    Results = documents
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }
        [NonAction]
        public async Task<T> GetJsonFromBlobUrl<T>(string blobUrl)
        {
            try
            {
                using var httpClient = new HttpClient();

                // Set timeout if needed
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                // Get the response
                HttpResponseMessage response = await httpClient.GetAsync(blobUrl);
                response.EnsureSuccessStatusCode();

                // Read the content
                string jsonContent = await response.Content.ReadAsStringAsync();

                // Deserialize with options
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                T result = JsonSerializer.Deserialize<T>(jsonContent, options);

                return result;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP Error: {ex.Message}");
                throw;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON Parsing Error: {ex.Message}");
                throw;
            }
        }

        [NonAction]
        // Helper method to clean JSON
        private string CleanJson(string json)
        {
            // Remove leading/trailing quotes if present
            json = json.Trim();
            if (json.StartsWith("\"") && json.EndsWith("\""))
            {
                json = json.Substring(1, json.Length - 2);
            }

            // Unescape JSON string
            json = System.Text.RegularExpressions.Regex.Unescape(json);

            return json;
        }

        [NonAction]
        public async Task PrintIndexSchema(string indexName)
        {
            try
            {
                   SearchIndexClient _indexClient;
        var azureAISearch = _configuration.GetSection("AISearch");
                var apiKey = azureAISearch["SearchApiKey"];
                var serviceEndpoint = azureAISearch["ServiceEndpoint"];
                var indexName1 = azureAISearch["SearchIndexName"];

                //if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(serviceEndpoint) || string.IsNullOrEmpty(indexName))
                    //return BadRequest("Missing configuration.");

                var credential = new AzureKeyCredential(apiKey);
                var searchClient = new SearchClient(new Uri(serviceEndpoint), indexName1, credential);

                _indexClient = new SearchIndexClient(
            new Uri(serviceEndpoint),
            new AzureKeyCredential(apiKey)
        );

                var index = await _indexClient.GetIndexAsync(indexName);

                Console.WriteLine($"╔══════════════════════════════════════════╗");
                Console.WriteLine($"║  Index: {index.Value.Name,-30} ║");
                Console.WriteLine($"╚══════════════════════════════════════════╝");
                Console.WriteLine();

                foreach (var field in index.Value.Fields)
                {
                    Console.WriteLine($"📄 {field.Name}");
                    Console.WriteLine($"   Type: {field.Type}");

                    var attributes = new List<string>();
                    if (field.IsKey == true) attributes.Add("Key");
                    if (field.IsSearchable == true) attributes.Add("Searchable");
                    if (field.IsFilterable == true) attributes.Add("Filterable");
                    if (field.IsSortable == true) attributes.Add("Sortable");
                    if (field.IsFacetable == true) attributes.Add("Facetable");

                    if (attributes.Any())
                        Console.WriteLine($"   Attributes: {string.Join(", ", attributes)}");

                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }
    }
}
