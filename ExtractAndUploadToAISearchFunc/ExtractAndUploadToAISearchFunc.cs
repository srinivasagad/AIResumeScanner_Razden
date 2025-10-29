using System.IO;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.AI.OpenAI;
using Azure;
using OpenAI.Chat;
using System.Text.RegularExpressions;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.Storage.Blobs.Models;
using ExtractAndUploadToAISearchFunc.Services;
using Azure.Storage.Blobs;
using System.Text.Json;

namespace ExtractAndUploadToAISearchFunc;

public class ExtractAndUploadToAISearchFunc
{
    private readonly ILogger<ExtractAndUploadToAISearchFunc> _logger;
    private readonly IConfiguration _configuration;

    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobServiceClient _blobServiceMetadataClient;
    private string blobContainerName { set; get; }
    private string blobMetadataContainerName { set; get; }
    private string blobMetadataContainerString { set; get; }
    private string content { set; get; }
    private readonly ChunkingService _chunkingService;
    private readonly EmbeddingService _embeddingService;
    public ExtractAndUploadToAISearchFunc(ILogger<ExtractAndUploadToAISearchFunc> logger, IConfiguration configuration, ChunkingService chunkingService, EmbeddingService embeddingService)
    {
       
        _logger = logger;
        _configuration = configuration;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;

        // Get the connection string  and container name  from configuration
        var blobConnectionString = _configuration.GetSection("BlobStorage")["ConnectionString"];
        _blobServiceClient = new BlobServiceClient(blobConnectionString);
       

        blobMetadataContainerString = _configuration.GetSection("MetaDataBlobStorage")["ConnectionString"];
        _blobServiceMetadataClient = new BlobServiceClient(blobMetadataContainerString);

        _logger.LogInformation("Constructor Calling.....");

    }

    [Function(nameof(ExtractAndUploadToAISearchFunc))]
    public async Task Run([BlobTrigger("resumes/{name}", Connection = "AzureWebJobsStorage")] Stream stream, string name)
    {
        blobMetadataContainerName = _configuration.GetSection("MetaDataBlobStorage")["ContainerName"];

        var extension = Path.GetExtension(name);
        string content = String.Empty;
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            content = ReadWordText(stream);
        }
        else if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            content = ReadPdfText(stream);
        }       

        _logger.LogInformation("C# Blob trigger function Processed blob\n Name: {name} \n Data: {content}", name, content);

        var azureOpenAISection = _configuration.GetSection("AzureOpenAI");
        var azureOpenAIEndPoint = azureOpenAISection["OpenAIEndPoint"];
        if (string.IsNullOrEmpty(azureOpenAIEndPoint))
        {
            _logger.LogInformation("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");            
            return;
        }

        var azureOpenAIKey = azureOpenAISection["OpenAIKey"];
        if (string.IsNullOrEmpty(azureOpenAIKey))
        {
            _logger.LogInformation("Please set the AZURE_OPENAI_KEY in app.settings.json");            
            return;
        }

        AzureKeyCredential credential = new AzureKeyCredential(azureOpenAIKey);

        // Initialize the AzureOpenAIClient
        AzureOpenAIClient azureClient = new(new Uri(azureOpenAIEndPoint), credential);

        var azureOpenAIDeploymentName = azureOpenAISection["DeploymentName"];
        if (string.IsNullOrEmpty(azureOpenAIDeploymentName))
        {
            _logger.LogInformation("Please set the DeploymentName in app.settings.json");            
            return;
        }
        // Initialize the ChatClient with the specified deployment name
        ChatClient chatClient = azureClient.GetChatClient(azureOpenAIDeploymentName);

        var resumePrompt = _configuration["ResumePrompt"];
        if (string.IsNullOrEmpty(resumePrompt))
        {
            _logger.LogInformation("Please set the prompt for resume in app.settings.json");
            return;
        }

        var messages = new List<ChatMessage>
                {
                   new SystemChatMessage(resumePrompt),
                   new UserChatMessage(content)
                };
        var azureOpenAITokenCount = _configuration.GetSection("AzureOpenAI")["TokenCount"];
        if (string.IsNullOrEmpty(azureOpenAITokenCount))
        {
            _logger.LogInformation("Please set the token count in app.settings.json");
            return;
        }
        var storageAccountName = _configuration.GetSection("DownloadBlobStorage")["AccountName"];
        if (string.IsNullOrEmpty(storageAccountName))
        {
            _logger.LogInformation("Please set the storage account name in app.settings.json");
            return;
        }
        var containerName = _configuration.GetSection("DownloadBlobStorage")["ContainerName"];
        if (string.IsNullOrEmpty(containerName))
        {
            _logger.LogInformation("Please set the container name in app.settings.json");
            return;
        }

        // Create chat completion options
        var options = new ChatCompletionOptions
        {
            Temperature = (float)0.7,
            MaxOutputTokenCount = Convert.ToInt32(azureOpenAITokenCount),

            TopP = (float)0.95,
            FrequencyPenalty = (float)0,
            PresencePenalty = (float)0
        };

        // Create the chat completion request
        ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);

        if (completion != null)
        {
            // Get the assistant's response content (the JSON string)
            string responseJson = completion.Content[0].Text.ToString();

            var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
            if (match.Success)
            {
                string cleanJson = match.Value;

                string dateTime = DateTime.UtcNow.ToString("o");
                DateTimeOffset dateTimeOffset = DateTimeOffset.UtcNow;
                string formattedDate = dateTimeOffset.ToString("o");

                var azureAISearch = _configuration.GetSection("AISearch");
                var apiKey = azureAISearch["SearchApiKey"];
                var serviceEndpoint = azureAISearch["ServiceEndpoint"];
                var indexName = azureAISearch["SearchIndexName"];

                var credentials = new AzureKeyCredential(apiKey);
                var searchClient = new SearchClient(
                    new Uri(serviceEndpoint),
                indexName,
                    credentials
                );

                var blobUrl = $"https://{storageAccountName}.blob.core.windows.net/{containerName}/{name}";

                // Step 1: Chunk the content
                var chunks = _chunkingService.ChunkBySentences(content);
                _logger.LogInformation($"Created {chunks.Count} chunks");


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
                    ["Title"] = name,
                    ["fileName"] = blobUrl,
                    ["Content"] = content,
                    ["chunks"] = chunks.ToArray(),
                    ["chunkVectors"] = averagedVector,
                    ["uploadDate"] = DateTimeOffset.UtcNow
                };
                string metaDataBlobUrl = await UploadJsonToBlobAsync(blobMetadataContainerString, blobMetadataContainerName, guid, cleanJson);
                document["metadata"] = metaDataBlobUrl;

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

    public async Task<string> UploadJsonToBlobAsync<T>(string connectionString, string containerName, string blobName, T data)
    {
        try
        {
            // Create blob client
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            // Create container if it doesn't exist
            await containerClient.CreateIfNotExistsAsync();

            var blobClient = containerClient.GetBlobClient(blobName);

            // Serialize object to JSON
            string jsonString = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Upload with proper content type
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/json"
                }
            };

            await blobClient.UploadAsync(
                BinaryData.FromString(jsonString),
                uploadOptions
            );

            Console.WriteLine($"Successfully uploaded {blobName}");
            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error uploading blob: {ex.Message}");
            return ex.Message;
        }
    }
}