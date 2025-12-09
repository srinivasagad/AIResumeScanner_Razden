using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents.Models;
using Azure.Search.Documents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Rest.Azure.OData;
using OpenAI.Chat;
using ResumeParserWebApi.Models;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Azure.Storage.Blobs;
using System.Reflection.PortableExecutable;
using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using DocumentFormat.OpenXml.Packaging;

namespace ResumeParserWebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AISearchFilterController : ControllerBase
    {
        private readonly ILogger<FileUploaderController> _logger;
        private readonly IConfiguration _configuration;
        public AISearchFilterController(ILogger<FileUploaderController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }




        [NonAction]
        public async Task<IActionResult> SearchByUserQuery([FromBody] string userQuery)
        {
            if (userQuery == null || string.IsNullOrEmpty(userQuery))
            {
                return BadRequest("Please provide user query.");
            }
            int count = 0;
            string filterString = String.Empty;
            var results = new List<FileSentimentResult>();
            var azureOpenAISection = _configuration.GetSection("AzureOpenAI");
            var azureOpenAIEndPoint = azureOpenAISection["OpenAIEndPoint"];
            if (string.IsNullOrEmpty(azureOpenAIEndPoint))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
                return BadRequest("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
            }

            var azureOpenAIKey = azureOpenAISection["OpenAIKey"];
            if (string.IsNullOrEmpty(azureOpenAIKey))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_KEY in app.settings.json");
                return BadRequest("Please set the AZURE_OPENAI_KEY in app.settings.json");
            }

            AzureKeyCredential credential = new AzureKeyCredential(azureOpenAIKey);

            // Initialize the AzureOpenAIClient  
            AzureOpenAIClient azureClient = new(new Uri(azureOpenAIEndPoint), credential);

            var azureOpenAIDeploymentName = azureOpenAISection["DeploymentName"];
            if (string.IsNullOrEmpty(azureOpenAIDeploymentName))
            {
                Console.WriteLine("Please set the DeploymentName in app.settings.json");
                return BadRequest("Please set the DeploymentName in app.settings.json");
            }

            var azureOpenAITokenCount = azureOpenAISection["TokenCount"];
            if (string.IsNullOrEmpty(azureOpenAITokenCount))
            {
                Console.WriteLine("Please set the token count in app.settings.json");
                return BadRequest("Please set the token count in app.settings.json");
            }

            // Initialize the ChatClient with the specified deployment name  
            ChatClient chatClient = azureClient.GetChatClient(azureOpenAIDeploymentName);

            string prompt = $@"  
                               Extract the following fields from the user query if present, and return as JSON:  
                               {{  
                                 ""FullName"": """",  
                                 ""Email"": """",  
                                 ""Phone"": """",  
                                 ""Location"": """",  
                                 ""ProfessionalSummary"": """",  
                                 ""FileName"": """",  
                                 ""ResumeUrl"": """",  
                                 ""ResumeId"": """",  
                                 ""Availability"": null,  
                                 ""IsActive"": null,  
                                 ""Skills"": [],  
                                 ""Languages"": [],  
                                 ""Certifications"": [],  
                                 ""MinTotalExperienceYears"": null,  
                                 ""MaxTotalExperienceYears"": null,  
                                 ""MinUploadDate"": null,  
                                 ""MaxUploadDate"": null  
                               }}  
                               User query: ""{userQuery}""  
                               ";

            var messages = new List<ChatMessage>
           {
               new SystemChatMessage(prompt)
           };

            // Create chat completion options  
            var options = new ChatCompletionOptions
            {
                Temperature = (float)0.7,
                MaxOutputTokenCount = Convert.ToInt32(azureOpenAITokenCount),
                TopP = (float)0.95,
                FrequencyPenalty = (float)0,
                PresencePenalty = (float)0
            };

            var completionResult = await chatClient.CompleteChatAsync(messages, options);

            if (completionResult != null && completionResult.Value != null)
            {
                // Get the assistant's response content (the JSON string)  
                string responseJson = completionResult.Value.Content[0].Text.ToString();

                if (string.IsNullOrEmpty(responseJson))
                {
                    return BadRequest("No response received from the AI model.");
                }

                //Clean the json
                var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                if (match.Success)
                {
                    responseJson = match.Value.Trim();
                    var filterRequest = JsonSerializer.Deserialize<SearchFilterRequest>(responseJson);

                    filterString = BuildAzureSearchFilter(filterRequest);

                    // Assuming filterRequest.Skills is a List<string>
                    var skills = filterRequest.Skills ?? new List<string>();

                    // Prefix each skill with '+' and join with spaces
                    string searchText = string.Join(" ", skills.Select(s => $"+{s}"));


                    var searchoptions = new SearchOptions
                    {
                        Filter = filterString
                        // Add other options as needed (e.g., Select, OrderBy, etc.)
                    };

                    var aiSearch = _configuration.GetSection("AISearch");
                    var serviceEndpoint = aiSearch["ServiceEndpoint"];
                    if (string.IsNullOrEmpty(serviceEndpoint))
                    {
                        Console.WriteLine("Please set the ServiceEndpoint in app.settings.json");
                        return BadRequest("Please set the ServiceEndpoint in app.settings.json");
                    }
                    var searchIndexName = aiSearch["SearchIndexName"];
                    if (string.IsNullOrEmpty(searchIndexName))
                    {
                        Console.WriteLine("Please set the SearchIndexName in app.settings.json");
                        return BadRequest("Please set the SearchIndexName in app.settings.json");
                    }
                    var searchApiKey = aiSearch["SearchApiKey"];
                    if (string.IsNullOrEmpty(searchApiKey))
                    {
                        Console.WriteLine("Please set the SearchApiKey in app.settings.json");
                        return BadRequest("Please set the SearchApiKey in app.settings.json");
                    }

                    var searchClient = new SearchClient(
                                                                    new Uri(serviceEndpoint),
                                                                            searchIndexName,
                                                           new AzureKeyCredential(searchApiKey)
                                                       );

                 
                    var response = await searchClient.SearchAsync<SearchDocument>(searchText, searchoptions);
                    var jsonResults = new List<string>();
                   
                    await foreach (var result in response.Value.GetResultsAsync())
                    {
                        results.Add(new FileSentimentResult
                        {
                            //FileName = "",
                            //AISentiment = result.Document
                        });
                        count++;
                    }
                    responseJson = $"[{string.Join(",", jsonResults)}]";

                }
                else
                {
                    return BadRequest("Invalid JSON response from the AI model.");
                }



                return Ok(new { Message = "Search filter applied successfully.", Query = userQuery,AISearchServiceQuery= filterString, ResumesRetrievedCount = count, Response = results });
            }

            return BadRequest("Failed to process the request.");
        }

        [NonAction]
        public async Task<IActionResult> MatchProfileWithQueryAndJobDescription([FromBody] MatchProfileRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.UserQuery) || string.IsNullOrEmpty(request.JobDescription))
            {
                return BadRequest("Please provide both user query and job description.");
            }
            var results = new List<FileSentimentResult>();
            string fileName = String.Empty;
            string aiResponse = String.Empty;
            string jobDescription = request.JobDescription; // _configuration["JobDescription"];
            int count = 0;

            string cleanJobDescription = jobDescription.Replace("\r\n", " ")
                                          .Replace("\n", " ")
                                          .Replace("\r", " ")
                                          .Trim();

            string filterString = String.Empty;

            var azureOpenAISection = _configuration.GetSection("AzureOpenAI");
            var azureOpenAIEndPoint = azureOpenAISection["OpenAIEndPoint"];
            if (string.IsNullOrEmpty(azureOpenAIEndPoint))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
                return BadRequest("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
            }

            var azureOpenAIKey = azureOpenAISection["OpenAIKey"];
            if (string.IsNullOrEmpty(azureOpenAIKey))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_KEY in app.settings.json");
                return BadRequest("Please set the AZURE_OPENAI_KEY in app.settings.json");
            }

            AzureKeyCredential credential = new AzureKeyCredential(azureOpenAIKey);

            // Initialize the AzureOpenAIClient  
            AzureOpenAIClient azureClient = new(new Uri(azureOpenAIEndPoint), credential);

            var azureOpenAIDeploymentName = azureOpenAISection["DeploymentName"];
            if (string.IsNullOrEmpty(azureOpenAIDeploymentName))
            {
                Console.WriteLine("Please set the DeploymentName in app.settings.json");
                return BadRequest("Please set the DeploymentName in app.settings.json");
            }

            var azureOpenAITokenCount = azureOpenAISection["TokenCount"];
            if (string.IsNullOrEmpty(azureOpenAITokenCount))
            {
                Console.WriteLine("Please set the token count in app.settings.json");
                return BadRequest("Please set the token count in app.settings.json");
            }

            // Initialize the ChatClient with the specified deployment name  
            ChatClient chatClient = azureClient.GetChatClient(azureOpenAIDeploymentName);

            string prompt = $@"  
                               Extract the following fields from the user query if present, and return as JSON:  
                               {{  
                                 ""FullName"": """",  
                                 ""Email"": """",  
                                 ""Phone"": """",  
                                 ""Location"": """",  
                                 ""ProfessionalSummary"": """",  
                                 ""FileName"": """",  
                                 ""ResumeUrl"": """",  
                                 ""ResumeId"": """",  
                                 ""Availability"": null,  
                                 ""IsActive"": null,  
                                 ""Skills"": [],  
                                 ""Languages"": [],  
                                 ""Certifications"": [],  
                                 ""MinTotalExperienceYears"": null,  
                                 ""MaxTotalExperienceYears"": null,  
                                 ""MinUploadDate"": null,  
                                 ""MaxUploadDate"": null  
                               }}  
                               User query: ""{request.JobDescription}""  
                               ";

            var messages = new List<ChatMessage>
           {
               new SystemChatMessage(prompt)
           };

            // Create chat completion options  
            var options = new ChatCompletionOptions
            {
                Temperature = (float)0.7,
                MaxOutputTokenCount = Convert.ToInt32(azureOpenAITokenCount),
                TopP = (float)0.95,
                FrequencyPenalty = (float)0,
                PresencePenalty = (float)0
            };

            var completionResult = await chatClient.CompleteChatAsync(messages, options);

            if (completionResult != null && completionResult.Value != null)
            {
                

                // Get the assistant's response content (the JSON string)  
                string responseJson = completionResult.Value.Content[0].Text.ToString();

                if (string.IsNullOrEmpty(responseJson))
                {
                    return BadRequest("No response received from the AI model.");
                }

                //Clean the json
                var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                if (match.Success)
                {
                    responseJson = match.Value.Trim();
                    var filterRequest = JsonSerializer.Deserialize<SearchFilterRequest>(responseJson);

                    filterString = BuildAzureSearchFilter(filterRequest);

                    // Assuming filterRequest.Skills is a List<string>
                    var skills = filterRequest.Skills ?? new List<string>();

                    // Prefix each skill with '+' and join with spaces
                    string searchText = string.Join(" | ", skills.Select(s => $"+{s}"));


                    var searchoptions = new SearchOptions
                    {
                        Filter = filterString
                        // Add other options as needed (e.g., Select, OrderBy, etc.)
                    };

                    var aiSearch = _configuration.GetSection("AISearch");
                    var serviceEndpoint = aiSearch["ServiceEndpoint"];
                    if (string.IsNullOrEmpty(serviceEndpoint))
                    {
                        Console.WriteLine("Please set the ServiceEndpoint in app.settings.json");
                        return BadRequest("Please set the ServiceEndpoint in app.settings.json");
                    }
                    var searchIndexName = aiSearch["SearchIndexName"];
                    if (string.IsNullOrEmpty(searchIndexName))
                    {
                        Console.WriteLine("Please set the SearchIndexName in app.settings.json");
                        return BadRequest("Please set the SearchIndexName in app.settings.json");
                    }
                    var searchApiKey = aiSearch["SearchApiKey"];
                    if (string.IsNullOrEmpty(searchApiKey))
                    {
                        Console.WriteLine("Please set the SearchApiKey in app.settings.json");
                        return BadRequest("Please set the SearchApiKey in app.settings.json");
                    }

                    var searchClient = new SearchClient(
                                                                    new Uri(serviceEndpoint),
                                                                            searchIndexName,
                                                           new AzureKeyCredential(searchApiKey)
                                                       );

                    //var response = await searchClient.SearchAsync<SearchDocument>("*", searchoptions);
                    var response = await searchClient.SearchAsync<SearchDocument>(searchText, searchoptions);
                    await foreach (var result in response.Value.GetResultsAsync())
                    {
                        try
                        {
                            result.Document.TryGetValue("file_name", out var file_name_value);
                            
                            fileName = file_name_value is string fileNameValue ? fileNameValue : "";
                            if (result.Document.TryGetValue("resume_url", out var value) && value is string blobUrl)
                            {
                                Console.WriteLine(blobUrl);
                                // Extract text from give resume URL 
                                string fileContent = await ReadBlobFromUrlAsync(blobUrl);
                                Console.WriteLine(fileContent);
                                // Run the AI model to match the job description with the resume
                                results.Add(new FileSentimentResult
                                {
                                    FileName = fileName,
                                    AISentiment = await RunAsync(cleanJobDescription, fileContent)
                                });
                                count++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading blob: {ex.Message}");

                        }
                    }
                   

                }
                else
                {
                    return BadRequest("Invalid JSON response from the AI model.");
                }



                return Ok(new { JobDescription = cleanJobDescription, Query = request.UserQuery, AISearchServiceQuery= filterString, ResumesRetrievedCount = count, Results = results });
            }

            return BadRequest("Failed to process the request.");
        }

        [HttpPost("GetSentimentAnalysisFilteredResumesByJobDescription")]
        public async Task<IActionResult> MatchProfileWithJobDescription([FromBody] string jobDescription)
        {
           
            var results = new List<FileSentimentResult>();
            string fileName = String.Empty;
            string aiResponse = String.Empty;            
            int count = 0;

            string filterString = String.Empty;

            var azureOpenAISection = _configuration.GetSection("AzureOpenAI");
            var azureOpenAIEndPoint = azureOpenAISection["OpenAIEndPoint"];
            if (string.IsNullOrEmpty(azureOpenAIEndPoint))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
                return BadRequest("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
            }

            var azureOpenAIKey = azureOpenAISection["OpenAIKey"];
            if (string.IsNullOrEmpty(azureOpenAIKey))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_KEY in app.settings.json");
                return BadRequest("Please set the AZURE_OPENAI_KEY in app.settings.json");
            }

            AzureKeyCredential credential = new AzureKeyCredential(azureOpenAIKey);

            // Initialize the AzureOpenAIClient  
            AzureOpenAIClient azureClient = new(new Uri(azureOpenAIEndPoint), credential);

            var azureOpenAIDeploymentName = azureOpenAISection["DeploymentName"];
            if (string.IsNullOrEmpty(azureOpenAIDeploymentName))
            {
                Console.WriteLine("Please set the DeploymentName in app.settings.json");
                return BadRequest("Please set the DeploymentName in app.settings.json");
            }

            var azureOpenAITokenCount = azureOpenAISection["TokenCount"];
            if (string.IsNullOrEmpty(azureOpenAITokenCount))
            {
                Console.WriteLine("Please set the token count in app.settings.json");
                return BadRequest("Please set the token count in app.settings.json");
            }

            // Initialize the ChatClient with the specified deployment name  
            ChatClient chatClient = azureClient.GetChatClient(azureOpenAIDeploymentName);

            string prompt = $@"  
                               Extract the following fields from the user query if present, and return as JSON:  
                               {{  
                                 ""FullName"": """",  
                                 ""Email"": """",  
                                 ""Phone"": """",  
                                 ""Location"": """",  
                                 ""ProfessionalSummary"": """",  
                                 ""FileName"": """",  
                                 ""ResumeUrl"": """",  
                                 ""ResumeId"": """",  
                                 ""Availability"": null,  
                                 ""IsActive"": null,  
                                 ""Skills"": [],  
                                 ""Languages"": [],  
                                 ""Certifications"": [],  
                                 ""MinTotalExperienceYears"": null,  
                                 ""MaxTotalExperienceYears"": null,  
                                 ""MinUploadDate"": null,  
                                 ""MaxUploadDate"": null  
                               }}  
                               User query: ""{jobDescription}""  
                               ";

            var messages = new List<ChatMessage>
           {
               new SystemChatMessage(prompt)
           };

            // Create chat completion options  
            var options = new ChatCompletionOptions
            {
                Temperature = (float)0.7,
                MaxOutputTokenCount = Convert.ToInt32(azureOpenAITokenCount),
                TopP = (float)0.95,
                FrequencyPenalty = (float)0,
                PresencePenalty = (float)0,
                
            };

            var completionResult = await chatClient.CompleteChatAsync(messages, options);

            if (completionResult != null && completionResult.Value != null)
            {
                // Get the assistant's response content (the JSON string)  
                string responseJson = completionResult.Value.Content[0].Text.ToString();

                if (string.IsNullOrEmpty(responseJson))
                {
                    return BadRequest("No response received from the AI model.");
                }

                //Clean the json
                var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                if (match.Success)
                {
                    responseJson = match.Value.Trim();
                    var filterRequest = JsonSerializer.Deserialize<SearchFilterRequest>(responseJson);

                    filterString = BuildAzureSearchFilter(filterRequest);



                    var searchoptions = new SearchOptions
                    {                        
                        Filter = filterString
                        // Add other options as needed (e.g., Select, OrderBy, etc.)
                    };

                    // Order by a field (e.g., descending by experience)
                    searchoptions.OrderBy.Add("total_experience_years desc");

                    // Highlight matches in the 'skills' field
                    //searchoptions.HighlightFields.Add("skills");

                    var aiSearch = _configuration.GetSection("AISearch");
                    var serviceEndpoint = aiSearch["ServiceEndpoint"];
                    if (string.IsNullOrEmpty(serviceEndpoint))
                    {
                        Console.WriteLine("Please set the ServiceEndpoint in app.settings.json");
                        return BadRequest("Please set the ServiceEndpoint in app.settings.json");
                    }
                    var searchIndexName = aiSearch["SearchIndexName"];
                    if (string.IsNullOrEmpty(searchIndexName))
                    {
                        Console.WriteLine("Please set the SearchIndexName in app.settings.json");
                        return BadRequest("Please set the SearchIndexName in app.settings.json");
                    }
                    var searchApiKey = aiSearch["SearchApiKey"];
                    if (string.IsNullOrEmpty(searchApiKey))
                    {
                        Console.WriteLine("Please set the SearchApiKey in app.settings.json");
                        return BadRequest("Please set the SearchApiKey in app.settings.json");
                    }

                    var searchClient = new SearchClient(
                                                                    new Uri(serviceEndpoint),
                                                                            searchIndexName,
                                                           new AzureKeyCredential(searchApiKey)
                                                       );

                    // Assuming filterRequest.Skills is a List<string>
                    var skills = filterRequest.Skills ?? new List<string>();

                    // Prefix each skill with '+' and join with spaces
                    string searchText = string.Join(" ", skills.Select(s => $"+{s}"));

                    var response = await searchClient.SearchAsync<SearchDocument>(searchText, searchoptions);
                    await foreach (var result in response.Value.GetResultsAsync())
                    {
                        try
                        {
                            result.Document.TryGetValue("file_name", out var file_name_value);

                            fileName = file_name_value is string fileNameValue ? fileNameValue : "";
                            if (result.Document.TryGetValue("resume_url", out var value) && value is string blobUrl)
                            {
                                Console.WriteLine(blobUrl);
                                // Extract text from give resume URL 
                                string fileContent = await ReadBlobFromUrlAsync(blobUrl);
                                //Console.WriteLine(fileContent);
                                var aiSentiment = await RunAsync(@jobDescription, fileContent);
                                // Run the AI model to match the job description with the resume
                                results.Add(new FileSentimentResult
                                {
                                    FileName = fileName,
                                    FileUrl= blobUrl,
                                    AISentiment = aiSentiment
                                });
                                count++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading blob: {ex.Message}");

                        }
                    }

                }
                else
                {
                    return BadRequest("Invalid JSON response from the AI model.");
                }

                return Ok(new { JobDescription = @jobDescription, Query = @jobDescription, AISearchServiceQuery = filterString, ResumesRetrievedCount = count, Results = results });
            }

            return BadRequest("Failed to process the request.");
        }

        [HttpPost("GetSentimentAnalysisFilteredResumesByJobDescriptionFromRESTAPI")]
        public async Task<IActionResult> MatchProfileWithJobDescriptionFromRESTApi([FromBody] string jobDescription)
        {

            var results = new List<FileSentimentResult>();
            string fileName = String.Empty;
            string aiResponse = String.Empty;
            int count = 0;

            string filterString = String.Empty;

            var azureOpenAISection = _configuration.GetSection("AzureOpenAI");
            var azureOpenAIEndPoint = azureOpenAISection["OpenAIEndPoint"];
            if (string.IsNullOrEmpty(azureOpenAIEndPoint))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
                return BadRequest("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
            }

            var azureOpenAIKey = azureOpenAISection["OpenAIKey"];
            if (string.IsNullOrEmpty(azureOpenAIKey))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_KEY in app.settings.json");
                return BadRequest("Please set the AZURE_OPENAI_KEY in app.settings.json");
            }

            AzureKeyCredential credential = new AzureKeyCredential(azureOpenAIKey);

            // Initialize the AzureOpenAIClient  
            AzureOpenAIClient azureClient = new(new Uri(azureOpenAIEndPoint), credential);

            var azureOpenAIDeploymentName = azureOpenAISection["DeploymentName"];
            if (string.IsNullOrEmpty(azureOpenAIDeploymentName))
            {
                Console.WriteLine("Please set the DeploymentName in app.settings.json");
                return BadRequest("Please set the DeploymentName in app.settings.json");
            }

            var azureOpenAITokenCount = azureOpenAISection["TokenCount"];
            if (string.IsNullOrEmpty(azureOpenAITokenCount))
            {
                Console.WriteLine("Please set the token count in app.settings.json");
                return BadRequest("Please set the token count in app.settings.json");
            }

            // Initialize the ChatClient with the specified deployment name  
            ChatClient chatClient = azureClient.GetChatClient(azureOpenAIDeploymentName);

            string prompt = $@"  
                               Extract the following fields from the user query if present, and return as JSON:  
                               {{  
                                 ""FullName"": """",  
                                 ""Email"": """",  
                                 ""Phone"": """",  
                                 ""Location"": """",  
                                 ""ProfessionalSummary"": """",  
                                 ""FileName"": """",  
                                 ""ResumeUrl"": """",  
                                 ""ResumeId"": """",  
                                 ""Availability"": null,  
                                 ""IsActive"": null,  
                                 ""Skills"": [],  
                                 ""Languages"": [],  
                                 ""Certifications"": [],  
                                 ""MinTotalExperienceYears"": null,  
                                 ""MaxTotalExperienceYears"": null,  
                                 ""MinUploadDate"": null,  
                                 ""MaxUploadDate"": null  
                               }}  
                               User query: ""{jobDescription}""  
                               ";

            var messages = new List<ChatMessage>
           {
               new SystemChatMessage(prompt)
           };

            // Create chat completion options  
            var chatCompletionOptions = new ChatCompletionOptions
            {
                Temperature = (float)0.7,
                MaxOutputTokenCount = Convert.ToInt32(azureOpenAITokenCount),
                TopP = (float)0.95,
                FrequencyPenalty = (float)0,
                PresencePenalty = (float)0,

            };

            var completionResult = await chatClient.CompleteChatAsync(messages, chatCompletionOptions);

            if (completionResult != null && completionResult.Value != null)
            {
                // Get the assistant's response content (the JSON string)  
                string responseJson = completionResult.Value.Content[0].Text.ToString();

                if (string.IsNullOrEmpty(responseJson))
                {
                    return BadRequest("No response received from the AI model.");
                }

                //Clean the json
                var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                if (match.Success)
                {
                    responseJson = match.Value.Trim();
                    var filterRequest = JsonSerializer.Deserialize<SearchFilterRequest>(responseJson);

                    filterString = BuildAzureSearchFilter(filterRequest);
                   

                    var aiSearch = _configuration.GetSection("AISearch");
                    var serviceEndpoint = aiSearch["ServiceEndpoint"];
                    if (string.IsNullOrEmpty(serviceEndpoint))
                    {
                        Console.WriteLine("Please set the ServiceEndpoint in app.settings.json");
                        return BadRequest("Please set the ServiceEndpoint in app.settings.json");
                    }
                    var searchIndexName = aiSearch["SearchIndexName"];
                    if (string.IsNullOrEmpty(searchIndexName))
                    {
                        Console.WriteLine("Please set the SearchIndexName in app.settings.json");
                        return BadRequest("Please set the SearchIndexName in app.settings.json");
                    }
                    var searchApiKey = aiSearch["SearchApiKey"];
                    if (string.IsNullOrEmpty(searchApiKey))
                    {
                        Console.WriteLine("Please set the SearchApiKey in app.settings.json");
                        return BadRequest("Please set the SearchApiKey in app.settings.json");
                    }

                    var searchApiVersion = aiSearch["SearchAPIVersion"];
                    if (string.IsNullOrEmpty(searchApiVersion))
                    {
                        Console.WriteLine("Please set the SearchAPIVersion in app.settings.json");
                        return BadRequest("Please set the SearchAPIVersion in app.settings.json");
                    }

                    

                    var endpoint = serviceEndpoint;
                    var indexName = searchIndexName;
                    var apiKey = searchApiKey;
                    var apiVersion = searchApiVersion; // or the version you use

                    var url = $"{endpoint}/indexes/{indexName}/docs/search?api-version={apiVersion}";

                    var request = new 
                    {                       
                        filter = filterString,                       
                        count = true,
                        select = "full_name,email,phone,resume_url,file_name",
                    };

                    string json = JsonSerializer.Serialize(request);

                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("api-key", apiKey);

                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(url, content);

                    string responseBody = await response.Content.ReadAsStringAsync();
                    // Console.WriteLine(responseBody);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var searchResponse = JsonSerializer.Deserialize<SearchResponse>(responseBody, options);

                    if (searchResponse?.value != null)
                    {
                        foreach (var doc in searchResponse.value)
                        {
                            Console.WriteLine($"Resume URL: {doc.resume_url}, File Name: {doc.file_name}");

                            // Parse the URL to get the path
                            var uri = new Uri(doc.resume_url);
                            string path = uri.AbsolutePath;


                            string extension = Path.GetExtension(path);
                            string fileContent = String.Empty;
                            if (extension == ".pdf")
                            {
                                fileContent = await ReadBlobFromUrlAsync(doc.resume_url);
                            }
                            if (extension == ".docx")
                            {
                                fileContent = await ReadWordText(doc.resume_url);
                            }
                            string skillsCsv = filterRequest.Skills != null
                                                                      ? string.Join(", ", filterRequest.Skills)
                                                                      : string.Empty;

                            var aiSentiment = await RunAsyncForRestApi(@jobDescription, skillsCsv, fileContent);

                            if (aiSentiment?.MatchWithJobDescription?.ToLower() != "low" && aiSentiment?.OverallSentiment?.ToLower()!="negative")
                            {
                                if (aiSentiment != null)
                                {
                                    results.Add(new FileSentimentResult
                                    {
                                        FullName = doc.full_name,
                                        Phone = doc.phone,
                                        Email = doc.email,
                                        FileName = doc.file_name,
                                        FileUrl = doc.resume_url,
                                        AISentiment = aiSentiment
                                    });
                                    count++;
                                }
                            }
                        }
                    }

                }
                else
                {
                    return BadRequest("Invalid JSON response from the AI model.");
                }

                return Ok(new { JobDescription = @jobDescription,  AISearchServiceQuery = filterString, ResumesRetrievedCount = count, Results = results });
            }

            return BadRequest("Failed to process the request.");
        }

        [NonAction]
        public static string BuildAzureSearchFilter(SearchFilterRequest filter)
        {
            var filters = new List<string>();

            if (!string.IsNullOrEmpty(filter.FullName))
                // filters.Add($"full_name eq '{filter.FullName.Replace("'", "''")}'");
                filters.Add($"search.ismatch('{filter.FullName.Replace("'", "''")}', 'full_name')");


            if (!string.IsNullOrEmpty(filter.Location))
                filters.Add($"location eq '{filter.Location.Replace("'", "''")}'");

            //if (filter.Skills != null && filter.Skills.Any())
            //    filters.Add(string.Join(" and ", filter.Skills.Select(skill => $"skills/any(s: s eq '{skill.Replace("'", "''")}')")));

            if (filter.Certifications != null && filter.Certifications.Any())
                filters.Add(string.Join(" and ", filter.Certifications.Select(cert => $"certifications/any(c: c eq '{cert.Replace("'", "''")}')")));

            if (filter.MinTotalExperienceYears.HasValue)
                filters.Add($"total_experience_years ge {filter.MinTotalExperienceYears.Value}");

            if (filter.MaxTotalExperienceYears.HasValue)
                filters.Add($"total_experience_years le {filter.MaxTotalExperienceYears.Value}");

            if (filter.WorkExperience != null && filter.WorkExperience.Any())
            {
                foreach (var we in filter.WorkExperience)
                {
                    var subFilters = new List<string>();
                    if (!string.IsNullOrEmpty(we.JobTitle))
                        subFilters.Add($"we.job_title eq '{we.JobTitle.Replace("'", "''")}'");
                    if (!string.IsNullOrEmpty(we.CompanyName))
                        subFilters.Add($"we.company_name eq '{we.CompanyName.Replace("'", "''")}'");
                    if (!string.IsNullOrEmpty(we.Location))
                        subFilters.Add($"we.location eq '{we.Location.Replace("'", "''")}'");
                    // Add more subfields as needed

                    if (subFilters.Count > 0)
                        filters.Add($"work_experience/any(we: {string.Join(" and ", subFilters)})");
                }
            }

            // Education nested filter
            if (filter.Education != null && filter.Education.Any())
            {
                foreach (var edu in filter.Education)
                {
                    var subFilters = new List<string>();
                    if (!string.IsNullOrEmpty(edu.Degree))
                        subFilters.Add($"e.degree eq '{edu.Degree.Replace("'", "''")}'");
                    if (!string.IsNullOrEmpty(edu.FieldOfStudy))
                        subFilters.Add($"e.field_of_study eq '{edu.FieldOfStudy.Replace("'", "''")}'");
                    if (!string.IsNullOrEmpty(edu.InstitutionName))
                        subFilters.Add($"e.institution_name eq '{edu.InstitutionName.Replace("'", "''")}'");
                    // Add more subfields as needed

                    if (subFilters.Count > 0)
                        filters.Add($"education/any(e: {string.Join(" and ", subFilters)})");
                }
            }

            // Projects nested filter
            if (filter.Projects != null && filter.Projects.Any())
            {
                foreach (var proj in filter.Projects)
                {
                    var subFilters = new List<string>();
                    if (!string.IsNullOrEmpty(proj.Title))
                        subFilters.Add($"p.title eq '{proj.Title.Replace("'", "''")}'");
                    if (!string.IsNullOrEmpty(proj.Description))
                        subFilters.Add($"p.description eq '{proj.Description.Replace("'", "''")}'");
                    if (proj.Technologies != null && proj.Technologies.Any())
                        subFilters.Add(string.Join(" and ", proj.Technologies.Select(tech => $"p.technologies/any(t: t eq '{tech.Replace("'", "''")}')")));
                    // Add more subfields as needed

                    if (subFilters.Count > 0)
                        filters.Add($"projects/any(p: {string.Join(" and ", subFilters)})");
                }
            }

           
            // Add this block for multiple skills
            if (filter.Skills != null && filter.Skills.Any())
            {
                var skillFilters = filter.Skills
                    .Select(skill => $"skills/any(s: s eq '{skill.Replace("'", "''")}')");
                filters.Add($"({string.Join(" or ", skillFilters)})");
            }

            return filters.Count > 0 ? string.Join(" and ", filters) : null;
        }

        [NonAction]
        public async Task<string> ReadWordText(string blobUrl)
        {
            var blobClient = new BlobClient(new Uri(blobUrl));

            using var memoryStream = new MemoryStream();
            await blobClient.DownloadToAsync(memoryStream);

            StringBuilder text = new StringBuilder();
            using (var wordDoc = WordprocessingDocument.Open(memoryStream, false))
            {
                var body = wordDoc.MainDocumentPart.Document.Body;
                text.Append(body.InnerText);
            }
            return text.ToString();
        }

        [NonAction]
        public async Task<string> ReadBlobFromUrlAsync(string blobUrl)
        {
            var blobClient = new BlobClient(new Uri(blobUrl));

            using var memoryStream = new MemoryStream();
            await blobClient.DownloadToAsync(memoryStream);
            memoryStream.Position = 0;
                       

            StringBuilder text = new StringBuilder();
            using (var pdfReader = new PdfReader(memoryStream))
            {
                using (var pdfDocument = new PdfDocument(pdfReader))
                {
                    for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
                    {
                        text.AppendLine(PdfTextExtractor.GetTextFromPage(pdfDocument.GetPage(i)));
                    }
                }
            }
            return text.ToString();
        }

        [NonAction]
        public async Task<AISentiment> RunAsync(string jobdescription, string resume)
        {
            string cleanJson = String.Empty;
            // Retrieve the OpenAI endpoint from environment variables
            var endpoint = "https://multiagent12345.openai.azure.com/";
            if (string.IsNullOrEmpty(endpoint))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_ENDPOINT environment variable.");
                
            }
            string requirementsList = "C#";

            var key = "Hto1XB1fcCaF2Bvy5M27t8fFkEKSzMqWStCp8HkanK0Jwt5SejIfJQQJ99BGACYeBjFXJ3w3AAABACOGu9Bi";
            if (string.IsNullOrEmpty(key))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_KEY environment variable.");
            }

            AzureKeyCredential credential = new AzureKeyCredential(key);

            // Initialize the AzureOpenAIClient
            AzureOpenAIClient azureClient = new(new Uri(endpoint), credential);

            // Initialize the ChatClient with the specified deployment name
            ChatClient chatClient = azureClient.GetChatClient("gpt-4o");


            // Fix for IDE0300: Simplify collection initialization.  
            var messages = new List<ChatMessage>
                {
                   new SystemChatMessage($@"You are an AI assistant. Given a job description and a resume, analyze if the resume matches the exact requirements in the job description.

                                        Required Skills:
                                        {requirementsList}

                                        For each requirement below, check if it is explicitly or implicitly present anywhere in the resume (including skills, work experience, education, certifications, projects, communication, Collaboration and adaptability skills, domain knowledge). For each, provide:
                                        - Requirement: The skill or communication or Collaboration and adaptability skills from the job description.
                                        - IsMatched: true if present, false if not, or null if unclear.
                                        - Evidence: The relevant text, section, or excerpt from any part of the resume that supports the match, or null if not found.


                                            Job Description
                                           -{jobdescription} 
                                                                               

                                          Provide the result in JSON format with the following fields:
                                        - OverallSentiment: The overall sentiment of the resume (e.g., ""Positive"", ""Negative"", or null if not determined).
                                        - ConfidenceScores: An object with Positive, Negative, and Neutral scores (numbers or null if not determined).
                                        - MatchWithJobDescription: A string describing how well the resume matches the job description (e.g., ""High"", ""Medium"", ""Low"", or null).
                                        - IsTailored: true if the resume is tailored to the job, false if not, or null if not determined.
                                        - Reasoning: A brief explanation of your analysis, or null if not available.
                                       - Requirements: An array of objects as described above, one for each skill  from the job description.
                                           
                                            If any value cannot be determined, return null for that field.

                                           
    
                                           "),
                   new UserChatMessage(resume)
                };


            // Create chat completion options

            var options = new ChatCompletionOptions
            {
                Temperature = (float)0.2,
                MaxOutputTokenCount = 8000,

                TopP = (float)0.95,
                FrequencyPenalty = (float)0,
                PresencePenalty = (float)0
            };

            try
            {
                // Create the chat completion request
                ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);

                // Print the response
                if (completion != null)
                {
                    //Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(completion, new JsonSerializerOptions() { WriteIndented = true }));

                    if (completion != null)
                    {
                        // Print token usage if available
                        if (completion.Usage != null)
                        {
                            Console.WriteLine($"Prompt used tokens: {completion.Usage.OutputTokenCount}");
                        }
                        else
                        {
                            Console.WriteLine("Token usage information is not available in the response.");
                        }
                    }

                    if (completion != null && completion.Content != null)
                    {
                        // Get the assistant's response content (the JSON string)
                        string response = completion.Content[0].Text.ToString();

                        // Get the assistant's response content (the JSON string)
                        string responseJson = completion.Content[0].Text.ToString();

                        var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                        if (match.Success)
                        {
                            cleanJson = match.Value;
                            Console.WriteLine(cleanJson);
                        }

                    }

                }
                else
                {
                    Console.WriteLine("No response received.");
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
            if (string.IsNullOrWhiteSpace(cleanJson))
            {
                Console.WriteLine("JSON string is null or empty.");
                return null;
            }
            return JsonSerializer.Deserialize<AISentiment>(cleanJson);
        }

        [NonAction]
        public async Task<AISentiment> RunAsyncForRestApi(string jobdescription, string requirementsList, string resume)
        {
            string cleanJson = String.Empty;
            // Retrieve the OpenAI endpoint from environment variables
            var endpoint = "https://multiagent12345.openai.azure.com/";
            if (string.IsNullOrEmpty(endpoint))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_ENDPOINT environment variable.");

            }
          

            var key = "Hto1XB1fcCaF2Bvy5M27t8fFkEKSzMqWStCp8HkanK0Jwt5SejIfJQQJ99BGACYeBjFXJ3w3AAABACOGu9Bi";
            if (string.IsNullOrEmpty(key))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_KEY environment variable.");
            }

            AzureKeyCredential credential = new AzureKeyCredential(key);

            // Initialize the AzureOpenAIClient
            AzureOpenAIClient azureClient = new(new Uri(endpoint), credential);

            // Initialize the ChatClient with the specified deployment name
            ChatClient chatClient = azureClient.GetChatClient("gpt-4o");


            // Fix for IDE0300: Simplify collection initialization.  
            var messages = new List<ChatMessage>
                {
                   new SystemChatMessage($@"You are an AI assistant. Given a job description and a resume, analyze if the resume matches the exact requirements in the job description.

                                        Required Skills:
                                        {requirementsList}                                       

                                        For each requirement below, check if it is explicitly or implicitly present anywhere in the resume (including skills, work experience, education, certifications, projects, communication skills, collaboration and adaptability skills, domain knowledge). For each, provide:
                                        - Requirement: The skill or requirement from the job description.
                                        - IsMatched: true if present, false if not, or null if unclear.
                                        - Evidence: The relevant text, section, or excerpt from any part of the resume that supports the match, or null if not found.


                                            Job Description
                                           -{jobdescription} 
                                                                               

                                          Provide the result in JSON format with the following fields:
                                        - OverallSentiment: The overall sentiment of the resume (e.g., ""Positive"", ""Negative"", or null if not determined).
                                        - ConfidenceScores: An object with Positive, Negative, and Neutral scores (numbers or null if not determined).
                                        - MatchWithJobDescription: A string describing how well the resume matches the job description (e.g., ""High"", ""Medium"", ""Low"", or null).
                                        - IsTailored: true if the resume is tailored to the job, false if not, or null if not determined.
                                        - Reasoning: A brief explanation of your analysis, or null if not available.
                                        - Requirements: An array of objects as described above, one for each skill or communication skills or collaboration and adaptability skills or domain knowledge from the job description.
                                           
                                            If any value cannot be determined, return null for that field.

                                           
    
                                           "),
                   new UserChatMessage(resume)
                };


            // Create chat completion options

            var options = new ChatCompletionOptions
            {
                Temperature = (float)0.2,
                MaxOutputTokenCount = 8000,

                TopP = (float)0.95,
                FrequencyPenalty = (float)0,
                PresencePenalty = (float)0
            };

            try
            {
                // Create the chat completion request
                ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);

                // Print the response
                if (completion != null)
                {
                    //Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(completion, new JsonSerializerOptions() { WriteIndented = true }));

                    if (completion != null)
                    {
                        // Print token usage if available
                        if (completion.Usage != null)
                        {
                            Console.WriteLine($"Prompt used tokens: {completion.Usage.OutputTokenCount}");
                        }
                        else
                        {
                            Console.WriteLine("Token usage information is not available in the response.");
                        }
                    }

                    if (completion != null && completion.Content != null)
                    {
                        // Get the assistant's response content (the JSON string)
                        string response = completion.Content[0].Text.ToString();

                        // Get the assistant's response content (the JSON string)
                        string responseJson = completion.Content[0].Text.ToString();

                        var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                        if (match.Success)
                        {
                            cleanJson = match.Value;
                            Console.WriteLine(cleanJson);
                        }

                    }

                }
                else
                {
                    Console.WriteLine("No response received.");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
            if (string.IsNullOrWhiteSpace(cleanJson))
            {
                Console.WriteLine("JSON string is null or empty.");
                return null;
            }
            return JsonSerializer.Deserialize<AISentiment>(cleanJson);
        }

    }
}
