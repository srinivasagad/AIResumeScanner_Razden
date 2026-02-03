using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using ResumeMetadataExtractFunc.Services;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResumeMetadataExtractFunc;

public class MetadataExtractToAISearchFunc
{
    private readonly ILogger<MetadataExtractToAISearchFunc> _logger;

    private readonly IConfiguration _configuration;
   
    private readonly ChunkingService _chunkingService;

    private readonly EmbeddingService _embeddingService;

    public MetadataExtractToAISearchFunc(ILogger<MetadataExtractToAISearchFunc> logger, IConfiguration configuration, ChunkingService chunkingService, EmbeddingService embeddingService)
    {
        _logger = logger;
        _configuration = configuration;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;        

        _logger.LogInformation("Constructor Calling.....");
    }

    [Function(nameof(MetadataExtractToAISearchFunc))]
    public async Task Run([BlobTrigger("resumes/{name}", Connection = "AzureWebJobsStorage")] Stream stream, string name)
    {
        try
        {
            var blobMetadataContainerName = _configuration["MetaDataBlobStorage:ContainerName"];
            var blobMetadataContainerString = _configuration["MetaDataBlobStorage:ConnectionString"];

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

            var azureOpenAIEndPoint = _configuration["AzureOpenAI:OpenAIEndPoint"];
            if (string.IsNullOrEmpty(azureOpenAIEndPoint))
            {
                _logger.LogInformation("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
                return;
            }

            var azureOpenAIKey = _configuration["AzureOpenAI:OpenAIKey"];
            if (string.IsNullOrEmpty(azureOpenAIKey))
            {
                _logger.LogInformation("Please set the AZURE_OPENAI_KEY in app.settings.json");
                return;
            }

            AzureKeyCredential credential = new AzureKeyCredential(azureOpenAIKey);

            // Initialize the AzureOpenAIClient
            AzureOpenAIClient azureClient = new(new Uri(azureOpenAIEndPoint), credential);

            var azureOpenAIDeploymentName = _configuration["AzureOpenAI:DeploymentName"];
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
            var azureOpenAITokenCount = _configuration["AzureOpenAI:TokenCount"];
            if (string.IsNullOrEmpty(azureOpenAITokenCount))
            {
                _logger.LogInformation("Please set the token count in app.settings.json");
                return;
            }
            var storageAccountName = _configuration["DownloadBlobStorage:AccountName"];
            if (string.IsNullOrEmpty(storageAccountName))
            {
                _logger.LogInformation("Please set the storage account name in app.settings.json");
                return;
            }
            var containerName = _configuration["DownloadBlobStorage:ContainerName"];
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

                    var apiKey = _configuration["AISearch:SearchApiKey"];
                    var serviceEndpoint = _configuration["AISearch:ServiceEndpoint"];
                    var indexName = _configuration["AISearch:SearchIndexName"];

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
                        _logger.LogInformation($"Successfully uploaded document: {name}, guid: {guid}");
                    }
                    else
                    {
                        _logger.LogInformation($"Failed to upload :{guid}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing blob: {name}", name);
            throw;
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
            _logger.LogError(ex, "Error processing blob: {blobName}", blobName);
            throw;
        }
    }

}
