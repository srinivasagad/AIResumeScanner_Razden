using Azure.Storage.Blobs;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text;
using Azure.Storage.Blobs.Models;
using Azure.Identity;
using Azure.AI.OpenAI;
using Azure;
using OpenAI.Chat;
using System.Text.RegularExpressions;
using Azure.Search.Documents;
using ResumeParserWebApi.Services;
using Azure.Search.Documents.Models;
using Newtonsoft.Json;
using ResumeParserWebApi.Models;

namespace ResumeParserWebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UpdateDashboardIndexController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<FileUploaderController> _logger;
         BlobServiceClient _blobServiceClient;
        private string blobContainerName { set; get; }
        public UpdateDashboardIndexController(ILogger<FileUploaderController> logger,IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost]
        public async Task<IActionResult>  Post()
        {
            var azureConnectionString = _configuration.GetSection("AzureBlobStorage")["ConnectionString"];
            BlobContainerClient containerClient = new BlobContainerClient(azureConnectionString, "resumes");

            // Check if container exists
            if (!await containerClient.ExistsAsync())
            {
                Console.WriteLine($"Container resumes does not exist.");               
            }
            else
            {
                Console.WriteLine($"Container resumes exist.");
            }
            int count = 0;
            await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
            {
                
                Console.WriteLine($"Name: {blobItem.Name}");
                Console.WriteLine($"Size: {blobItem.Properties.ContentLength} bytes");

                // Download and read content
                BlobClient blobClient = containerClient.GetBlobClient(blobItem.Name);

                string content = string.Empty;
                using (Stream stream = await blobClient.OpenReadAsync())
                {
                    if (blobItem.Name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                    {
                        string wordText = ReadWordText(stream);
                        content = wordText;
                        //Console.WriteLine($"Content: {wordText}...");
                    }
                    else if (blobItem.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        string pdfText = ReadPdfText(stream);
                        content = pdfText;
                        //Console.WriteLine($"Content: {pdfText}...");
                    }
                }

                var azureOpenAISection = _configuration.GetSection("AzureOpenAI");
                var azureOpenAIEndPoint = azureOpenAISection["OpenAIEndPoint"];
                if (string.IsNullOrEmpty(azureOpenAIEndPoint))
                {
                    _logger.LogInformation("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
                }

                var azureOpenAIKey = azureOpenAISection["OpenAIKey"];
                if (string.IsNullOrEmpty(azureOpenAIKey))
                {
                    _logger.LogInformation("Please set the AZURE_OPENAI_KEY in app.settings.json");
                }

                AzureKeyCredential credential = new AzureKeyCredential(azureOpenAIKey);

                // Initialize the AzureOpenAIClient
                AzureOpenAIClient azureClient = new(new Uri(azureOpenAIEndPoint), credential);

                var azureOpenAIDeploymentName = azureOpenAISection["DeploymentName"];
                if (string.IsNullOrEmpty(azureOpenAIDeploymentName))
                {
                    _logger.LogInformation("Please set the DeploymentName in app.settings.json");
                }
                // Initialize the ChatClient with the specified deployment name
                ChatClient chatClient = azureClient.GetChatClient(azureOpenAIDeploymentName);

                var resumePrompt = string.Empty; //_configuration["ResumePrompt"];


                resumePrompt = @"
You are a resume parsing AI. Extract information from the provided resume and return it in the exact JSON structure specified below. Follow these rules strictly:\n\n1. Extract all available information accurately from the resume\n2. All specified fields MUST be present in the output, even if empty or null\n3. For dates, use format: YYYY-MM or YYYY (extract whatever is available)\n4. For work experience, calculate date ranges and extract all job details\n5. For total_experience_years, calculate from work history or extract if stated\n6. Classify the resume into ONE category: Software Development, Data Science, DevOps, UI/UX Design, or QA Engineering\n7. Determine the most appropriate job title from the predefined list based on the candidate's experience and skills\n8. Return ONLY valid JSON with no additional text, explanations, or markdown\n\nRequired JSON structure:\n{\n  \""full_name\"": \""string or null\"",\n  \""email\"": \""string or null\"",\n  \""phone\"": \""string or null\"",\n  \""location\"": \""string or null\"",\n  \""professional_summary\"": \""string or null\"",\n  \""skills\"": [],\n  \""total_experience_years\"": \""number or null\"",\n  \""work_experience\"": [\n    {\n      \""job_title\"": \""string\"",\n      \""company_name\"": \""string\"",\n      \""start_date\"": \""string\"",\n      \""end_date\"": \""string or 'Present'\"",\n      \""location\"": \""string or null\"",\n      \""description\"": \""string or null\""\n    }\n  ],\n  \""education\"": [\n    {\n      \""degree\"": \""string\"",\n      \""field_of_study\"": \""string or null\"",\n      \""institution_name\"": \""string\"",\n      \""start_date\"": \""string or null\"",\n      \""end_date\"": \""string or null\""\n    }\n  ],\n  \""certifications\"": [\n    {\n      \""name\"": \""string\"",\n      \""issuer\"": \""string or null\"",\n      \""date\"": \""string or null\""\n    }\n  ],\n  \""projects\"": [\n    {\n      \""title\"": \""string\"",\n      \""description\"": \""string or null\"",\n      \""technologies\"": \""string or null\""\n    }\n  ],\n  \""languages\"": [],\n  \""category\"": \""one of: Software Development | Data Science | DevOps | UI/UX Design | QA Engineering\"",\n  \""title\"": \""one of: Senior Developer | Software Engineer | Full Stack Developer | Backend Developer | Frontend Developer | DevOps Engineer | Data Scientist | ML Engineer\""\n}\n\nNow parse the resume and return the structured JSON output.
                                ";

                var messages = new List<ChatMessage>
                {
                   new SystemChatMessage(resumePrompt),
                   new UserChatMessage(content)
                };
                var options = new ChatCompletionOptions()
                {
                    Temperature = (float)1,
                    FrequencyPenalty = (float)0,
                    PresencePenalty = (float)0
                };

                ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);

                string cleanJson = string.Empty;
                if (completion != null)
                {
                    // Get the assistant's response content (the JSON string)
                    string responseJson = completion.Content[0].Text.ToString();

                    var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                    if (match.Success)
                    {
                        cleanJson = match.Value;
                        // Deserialize
                        Resume resume = JsonConvert.DeserializeObject<Resume>(cleanJson);



                        string dateTime = DateTime.UtcNow.ToString("o");
                        DateTimeOffset dateTimeOffset = DateTimeOffset.UtcNow;
                        string formattedDate = dateTimeOffset.ToString("o");

                        var azureAISearch = _configuration.GetSection("AISearch");
                        var apiKey = azureAISearch["SearchApiKey"];
                        var serviceEndpoint = azureAISearch["ServiceEndpoint"];
                        var indexName = azureAISearch["SearchDashboardIndexName"];

                        var credentials = new AzureKeyCredential(apiKey);
                        var searchClient = new SearchClient(
                            new Uri(serviceEndpoint),
                        indexName,
                            credentials
                        );

                        ChunkingService chunkingService = new ChunkingService();
                        // Step 1: Chunk the content
                        var chunks = chunkingService.ChunkBySentences(content);
                        _logger.LogInformation($"Created {chunks.Count} chunks");

                        EmbeddingService _embeddingService = new EmbeddingService();
                        // Step 2: Generate embeddings for each chunk
                        var chunkVectors = await _embeddingService.GenerateEmbeddingsForChunksAsync(chunks);
                        _logger.LogInformation($"Generated {chunkVectors.Count} embeddings");

                        var averagedVector = await _embeddingService.AverageVectors(chunkVectors);

                        // Step 3: Create document with chunks and custom fields
                        // Create document with dynamic custom fields
                        string guid = System.Guid.NewGuid().ToString();

                        var document = new SearchDocument
                        {
                            ["Id"] = guid,
                            ["Title"] = resume.Title,
                            ["Name"] = resume.FullName,
                            ["Email"] = resume.Email,
                            ["Category"] = resume.Category,
                            ["ExperienceYears"] = resume.TotalExperienceYears,
                            ["Location"] = resume.Location,
                            ["Skills"] = resume.Skills,
                            ["Content"] = content,
                            ["chunks"] = chunks.ToArray(),
                            ["chunkVectors"] = averagedVector,
                            ["UploadDate"] = DateTimeOffset.UtcNow
                        };

                        var batch = IndexDocumentsBatch.Upload(new[] { document });
                        var uploadResult = await searchClient.IndexDocumentsAsync(batch);
                        _logger.LogInformation("Updated document to AI Search Index");
                        if (uploadResult.Value.Results[0].Succeeded)
                        {
                            _logger.LogInformation($"Successfully uploaded document {guid} with {chunks.Count} chunks");
                        }
                        else
                        {
                            _logger.LogInformation($"Failed to upload :{guid}");
                        }

                    }
                }

            
          

            }




            // Logic to update the dashboard index goes here
            return Ok("Dashboard index updated successfully.");
        }
        [NonAction]
        public static string ReadWordText(Stream docxStream)
        {
            StringBuilder text = new StringBuilder();
            using (var wordDoc = WordprocessingDocument.Open(docxStream, false))
            {
                var body = wordDoc.MainDocumentPart.Document.Body;
                text.Append(body.InnerText);
            }
            return text.ToString();
        }

        [NonAction]
        public static string ReadPdfText(Stream pdfStream)
        {
            StringBuilder text = new StringBuilder();
            using (var pdfReader = new PdfReader(pdfStream))
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

    }
}
