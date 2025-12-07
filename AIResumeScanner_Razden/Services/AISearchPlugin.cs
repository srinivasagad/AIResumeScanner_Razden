using Azure;
using Azure.AI.OpenAI;
using Azure.AI.TextAnalytics;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using AIResumeScanner_Razden.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenAI.Chat;
using OpenAI;
using System.Text.Json;
using System.Text.RegularExpressions;
using iTextSharp.text;

namespace AIResumeScanner_Razden.Services
{
    public class AISearchPlugin
    {

        private readonly SearchClient _searchClient;
        public IConfiguration _configuration;

        public AISearchPlugin(string endpoint, string indexName, string apiKey)
        {
            _searchClient = new SearchClient(
                new Uri(endpoint),
                indexName,
                new AzureKeyCredential(apiKey)
            );

            var builder = new ConfigurationBuilder()
                         .SetBasePath(Directory.GetCurrentDirectory())
                         .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                         .AddEnvironmentVariables();
            _configuration = builder.Build();
        }

        [KernelFunction, Description("Perform semantic search using vector embeddings")]
        public async Task<string> SemanticSearch(
            [Description("The search query")] string query,
            [Description("Number of results")] int top = 10)
        {
            try
            {
                var queryEmbedding = await GenerateEmbedding(query);
                // For production, you'd generate embeddings using Azure OpenAI
                // This is a simplified example
                var searchOptions = new SearchOptions
                {
                    Size = top,
                    //Select = { "Id", "Title", "chunks", "Content", "fileName" },
                    Select = { "Id", "chunks", "Content", "fileName" },
                    IncludeTotalCount = true,
                    HighlightFields = { "chunks", "Content" },
                    QueryType = SearchQueryType.Semantic,
                    SemanticSearch = new()
                    {
                        SemanticConfigurationName = "semantic",
                        QueryCaption = new(QueryCaptionType.Extractive),
                        QueryAnswer = new(QueryAnswerType.Extractive)
                    },
                    VectorSearch = new()
                    {
                        Queries =
                        {
                            new VectorizedQuery(queryEmbedding)
                            {
                                KNearestNeighborsCount=top ,
                                Fields={ "chunkVectors" }
                            }
                        }
                    }
                };

                var response = await _searchClient.SearchAsync<Azure.Search.Documents.Models.SearchDocument>(query, searchOptions);


                return FormatSearchResults(response);

                /*
                // Add sentiment analysis to results
                var resultsWithSentimentWithGPT = await AddSentimentAnalysisWithGPT(response, query);
                return FormatSearchResults(resultsWithSentimentWithGPT, response.Value.TotalCount ?? 0);
                */

            }
            catch (Exception ex)
            {
                return $"Search error: {ex.Message}";
            }
        }

        //SearchResults<Azure.Search.Documents.Models.SearchDocument> results
        private string FormatSearchResults(SearchResults<Azure.Search.Documents.Models.SearchDocument> results)
        {
            var formattedResults = new StringBuilder();
            formattedResults.AppendLine("Search Results (ordered by relevance):\n");

            int rank = 1;
            foreach (var result in results.GetResults())
            {
                // Calculate star rating based on Semantic search re-ranker score or search score
                string starRating = GetStarRating(result.SemanticSearch.RerankerScore ?? result.Score ?? 0);

                formattedResults.AppendLine($"📄 Result #{rank} {starRating}");
                formattedResults.AppendLine("───────────────────────────────────────────────────────");

                

                // Get reranker score if available
                double? rerankerScore = result.SemanticSearch.RerankerScore;
                double? searchScore = result.Score;

                formattedResults.AppendLine($"--- Result #{rank} ---");

                

                // Display scores with visual indicators
                if (result.SemanticSearch.RerankerScore.HasValue)
                {
                    string scoreBar = GetScoreBar(result.SemanticSearch.RerankerScore.Value);
                    formattedResults.AppendLine($"\n🎯 Relevance: {scoreBar} ({result.SemanticSearch.RerankerScore.Value:F2})");
                }
                else if (result.Score.HasValue)
                {
                    string scoreBar = GetScoreBar(result.Score.Value);
                    formattedResults.AppendLine($"🎯 Score: {scoreBar} ({result.Score.Value:F2})");
                }

                // Extract and display profile information
                var document = result.Document;

                if (document.ContainsKey("Title"))
                    formattedResults.AppendLine($" 📌 Title: {document["Title"]}");

                if (document.ContainsKey("Content"))
                    formattedResults.AppendLine($" \n📚 Content: {document["Content"]}");
                

                // Chunks with better formatting
                if (document.ContainsKey("chunks"))
                {
                    formattedResults.AppendLine($"\n📚 Chunk Sections ({document["chunks"]} total):");
                    var chunks = document["chunks"];
                    List<string> chunkTexts = new();

                    // If chunks is a JsonElement (from Azure Search)
                    if (chunks is JsonElement jsonElement)
                    {
                        if (jsonElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var chunk in jsonElement.EnumerateArray())
                            {
                                if (chunk.ValueKind == JsonValueKind.String)
                                    chunkTexts.Add(chunk.GetString());
                                else if (chunk.ValueKind == JsonValueKind.Object && chunk.TryGetProperty("text", out var textElement))
                                    chunkTexts.Add(textElement.GetString());
                            }
                        }
                        else if (jsonElement.ValueKind == JsonValueKind.String)
                        {
                            chunkTexts.Add(jsonElement.GetString());
                        }
                    }
                    // If chunks is an object array
                    else if (chunks is object[] objArr)
                    {
                        foreach (var item in objArr)
                        {
                            if (item != null)
                                chunkTexts.Add(item.ToString());
                        }
                    }
                    // If chunks is a string array
                    else if (chunks is string[] strArr)
                    {
                        chunkTexts.AddRange(strArr.Where(s => !string.IsNullOrEmpty(s)));
                    }
                    // Fallback: single string
                    else if (chunks is string str)
                    {
                        chunkTexts.Add(str);
                    }

                    // Display the extracted chunk texts
                    foreach (var text in chunkTexts)
                    {
                        formattedResults.AppendLine($"   ▸ {text}");
                    }

                }

                // Add highlights if available
                if (result.Highlights != null && result.Highlights.Any())
                {
                    formattedResults.AppendLine("Highlights:");
                    foreach (var highlight in result.Highlights)
                    {
                        formattedResults.AppendLine($" 💡 {highlight.Key}: {string.Join(", ", highlight.Value)}");
                    }
                }

                // fileName URL
                if (document.ContainsKey("fileName"))
                    formattedResults.AppendLine($"\n🔗 Source: <a href=\"{document["fileName"]}\" target=\"_blank\">Source</a>");

                formattedResults.AppendLine("\n═══════════════════════════════════════════════════════\n");
                rank++;
            }

            return formattedResults.ToString();
        }

        private async Task<List<SearchResultModel>> AddSentimentAnalysisWithGPT(SearchResults<Azure.Search.Documents.Models.SearchDocument> results, string userQuery)
        {
            var parsedResults = ParseSearchResults(results);

            ConfidenceScores confidenceScores;
            var filteredResults = parsedResults
                                    .GroupBy(r => r.FileName)
                                    .Select(g => g.First())
                                    .ToList();
            try
            {
                foreach (var result in filteredResults)
                {
                    // Get content for analysis  
                    var contentForAnalysis = !string.IsNullOrEmpty(result.Content)
                        ? result.Content
                        : result.Chunks.Any()
                            ? string.Join(" ", result.Chunks)
                            : "";

                    if (!string.IsNullOrEmpty(contentForAnalysis))
                    {

                        var sentiment = await AnalyzeSentimentWithGPT4(contentForAnalysis, userQuery);

                        result.IsTailored = sentiment.IsTailored;
                        result.MatchWithJobDescription = sentiment.MatchWithJobDescription;

                        // Map RequirementModel to Requirement  
                        List<Requirement> requirements = sentiment.Requirements
                            .Select(req => new Requirement
                            {
                                requirement = req.Requirement,
                                isMatched = req.IsMatched,
                                evidence = req.Evidence
                            }).ToList();

                        result.Requirements = requirements;
                        result.Reasoning = sentiment.Reasoning;
                        result.Sentiment = sentiment.Sentiment;
                        confidenceScores = new ConfidenceScores();
                        confidenceScores.Positive = (float)sentiment.PositiveScore;
                        confidenceScores.Neutral = (float)sentiment.NeutralScore;
                        confidenceScores.Negative = (float)sentiment.NegativeScore;
                        result.ConfidenceScores = confidenceScores;
                    }
                }

                return filteredResults;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sentiment analysis error: {ex.Message}");
                return filteredResults;
            }
        }

        private async Task<SentimentResult> AnalyzeSentimentWithGPT4(string text, string userQuery)
        {
            var resumePrompt = $@"
                                    You are an AI assistant. Given a job description and a resume, analyze if the resume matches the exact requirements in the job description.

                                    For each requirement below, check if it is explicitly or implicitly present anywhere in the resume (including skills, work experience, education, certifications, projects, communication skills, collaboration and adaptability skills, domain knowledge). For each, provide:
                                        - Requirement: The skill or requirement from the job description.
                                        - IsMatched: true if present, false if not, or null if unclear.
                                        - Evidence: The relevant text, section, or excerpt from any part of the resume that supports the match, or null if not found.


                                            Job Description
                                           -{userQuery} 
                                                                               

                                          Provide the result in JSON format with the following fields:
                                        - OverallSentiment: The overall sentiment of the resume (e.g., ""Positive"", ""Negative"", or null if not determined).
                                        
                                            If any value cannot be determined, return null for that field.
                              ";

            string resumeText = $"{text}";
            var azureOpenAITokenCount = _configuration.GetSection("AzureOpenAI")["ChatTokenCount"];
            if (string.IsNullOrEmpty(azureOpenAITokenCount))
            {
                Console.WriteLine("Please set the token count in app.settings.json");
            }
            var messages = new List<OpenAI.Chat.ChatMessage>
                                                        {
                                                           new SystemChatMessage(resumePrompt),
                                                           new UserChatMessage(resumeText)
                                                        };

            // Create chat completion options
            var options = new ChatCompletionOptions
            {
                Temperature = (float)0.1,
                MaxOutputTokenCount = Convert.ToInt32(azureOpenAITokenCount),

                TopP = (float)0.95,
                FrequencyPenalty = (float)0,
                PresencePenalty = (float)0
            };


            var azureOpenAISection = _configuration.GetSection("AzureOpenAI");
            var azureOpenAIEndPoint = azureOpenAISection["OpenAIEndPoint"];
            if (string.IsNullOrEmpty(azureOpenAIEndPoint))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_ENDPOINT in app.settings.json");
            }

            var azureOpenAIKey = azureOpenAISection["OpenAIKey"];
            if (string.IsNullOrEmpty(azureOpenAIKey))
            {
                Console.WriteLine("Please set the AZURE_OPENAI_KEY in app.settings.json");
            }

            var azureOpenAIDeploymentName = azureOpenAISection["ChatDeploymentName"];
            if (string.IsNullOrEmpty(azureOpenAIDeploymentName))
            {
                Console.WriteLine("Please set the DeploymentName in app.settings.json");
            }

            AzureKeyCredential credential = new AzureKeyCredential(azureOpenAIKey);

            // Initialize the AzureOpenAIClient
            AzureOpenAIClient azureClient = new(new Uri(azureOpenAIEndPoint), credential);
            // Initialize the ChatClient with the specified deployment name
            ChatClient chatClient = azureClient.GetChatClient(azureOpenAIDeploymentName);
            // Create the chat completion request
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
                }
            }
            return ParseSentimentResponse(cleanJson);
        }

        private SentimentResult ParseSentimentResponse(string jsonResponse)
        {
            try
            {
                // Clean up the response in case GPT-4 adds markdown formatting
                jsonResponse = jsonResponse.Trim();
                if (jsonResponse.StartsWith("```json"))
                    jsonResponse = jsonResponse.Substring(7);
                if (jsonResponse.StartsWith("```"))
                    jsonResponse = jsonResponse.Substring(3);
                if (jsonResponse.EndsWith("```"))
                    jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3);
                jsonResponse = jsonResponse.Trim();

                var sentimentData = JsonSerializer.Deserialize<SentimentJsonResponse>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return new SentimentResult
                {
                    MatchWithJobDescription = sentimentData.MatchWithJobDescription,
                    IsTailored = sentimentData.IsTailored,
                    Reasoning = sentimentData.Reasoning,
                    Requirements = sentimentData.Requirements,
                    Sentiment = sentimentData.OverallSentiment,
                    PositiveScore = sentimentData.PositiveScore,
                    NeutralScore = sentimentData.NeutralScore,
                    NegativeScore = sentimentData.NegativeScore                  
                    
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing sentiment response: {ex.Message}");
                // Return neutral sentiment as fallback
                return new SentimentResult
                {
                    Sentiment = "Neutral",
                    PositiveScore = 0.0,
                    NeutralScore = 1.0,
                    NegativeScore = 0.0
                };
            }
        }

      
        public async Task<float[]> GenerateEmbedding(string text)
        {
            string endpoint = "https://resumeembeddingendpoint.openai.azure.com/";
            string apiKey = "BxUQYM8ND9UR2q3WqFrk2YlyHR4NHCG2ORy6xpublVSY4WIl3TwYJQQJ99BJACYeBjFXJ3w3AAABACOGeGiz";
            string deploymentName = "text-embedding-ada-002"; // or your custom deployment name

            var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            var embeddingClient = client.GetEmbeddingClient(deploymentName);

            // Generate embeddings
            var embeddingsResult = await embeddingClient.GenerateEmbeddingsAsync(
                new List<string> { text }
            );

            // Fix: Access the embeddings from the Value property, which is an OpenAIEmbeddingCollection
            var embeddingCollection = embeddingsResult.Value;
            if (embeddingCollection != null && embeddingCollection.Count > 0)
            {
                // Each OpenAIEmbedding has a Vector property (float[])
                return embeddingCollection[0].ToFloats().ToArray();
            }
            return Array.Empty<float>();
        }
              
        private List<SearchResultModel> ParseSearchResults(SearchResults<Azure.Search.Documents.Models.SearchDocument> results)
        {
            var searchResults = new List<SearchResultModel>();
            int rank = 1;

            foreach (var result in results.GetResults())
            {
                var model = new SearchResultModel
                {
                    Rank = rank,
                    SearchScore = result.Score,
                    RerankerScore = result.SemanticSearch?.RerankerScore
                };

                // Extract document fields
                if (result.Document.TryGetValue("id", out var id))
                    model.Id = id?.ToString();

                if (result.Document.TryGetValue("title", out var title))
                    model.Title = title?.ToString();

                if (result.Document.TryGetValue("content", out var content))
                    model.Content = content?.ToString();

                if (result.Document.TryGetValue("fileName", out var fileName))
                    model.FileName = fileName?.ToString();

                if (result.Document.TryGetValue("category", out var category))
                    model.Category = category?.ToString();

                // Extract chunks
                if (result.Document.TryGetValue("chunks", out var chunks))
                {
                    string[] chunkArray = chunks switch
                    {
                        string[] strArr => strArr,
                        object[] objArr => objArr.Select(o => o?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray(),
                        _ => null
                    };

                    if (chunkArray != null)
                        model.Chunks = chunkArray.ToList();
                }

                // Extract skills
                if (result.Document.TryGetValue("skills", out var skills))
                {
                    string[] skillArray = skills switch
                    {
                        string[] strArr => strArr,
                        object[] objArr => objArr.Select(o => o?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray(),
                        _ => null
                    };

                    if (skillArray != null)
                        model.Skills = skillArray.ToList();
                }

                // Extract semantic captions
                if (result.SemanticSearch?.Captions != null)
                {
                    model.SemanticCaptions = result.SemanticSearch.Captions
                        .Select(c => new SemanticCaption { Text = c.Text })
                        .ToList();
                }

                // Extract highlights
                if (result.Highlights != null)
                {
                    model.Highlights = result.Highlights
                        .ToDictionary(h => h.Key, h => h.Value.ToList());
                }

                // Extract metadata
                if (result.Document.TryGetValue("metadata", out var metadata))
                    model.MetadataUrl = metadata?.ToString();

                if (result.Document.TryGetValue("uploadDate", out var uploadDate))
                {
                    if (uploadDate is DateTimeOffset dto)
                        model.UploadDate = dto;
                }

                searchResults.Add(model);
                rank++;
            }

            return searchResults;
        }


        // Usage
       

        private string FormatSearchResults(List<SearchResultModel> results, long totalCount)
        {
            var formatted = new StringBuilder();
            int positiveCount = results.Count(r => string.Equals(r.Sentiment, "Positive", StringComparison.OrdinalIgnoreCase));


            formatted.AppendLine($"🔍 Found {positiveCount} relevant profiles\n");
            formatted.AppendLine("═══════════════════════════════════════════════════════\n");

            foreach (var result in results)
            {
                if (result.Sentiment != null && result.Sentiment.ToString().ToLower() == "positive")
                {


                    // Calculate star rating based on reranker score or search score
                    string starRating = GetStarRating(result.RerankerScore ?? result.SearchScore ?? 0);

                    formatted.AppendLine($"📄 Result #{result.Rank} {starRating}");
                    formatted.AppendLine("───────────────────────────────────────────────────────");

                    if (!string.IsNullOrEmpty(result.Title))
                        formatted.AppendLine($"📌 Title: {result.Title}");

                    //if (!string.IsNullOrEmpty(result.FileName))
                    //    formatted.AppendLine($"📁 File: {result.FileName}");

                    if (!string.IsNullOrEmpty(result.Category))
                        formatted.AppendLine($"🏷️  Category: {result.Category}");

                    if (result.Skills.Any())
                        formatted.AppendLine($"💼 Skills: {string.Join(", ", result.Skills)}");




                    // Display scores with visual indicators
                    if (result.RerankerScore.HasValue)
                    {
                        string scoreBar = GetScoreBar(result.RerankerScore.Value);
                        formatted.AppendLine($"\n🎯 Relevance: {scoreBar} ({result.RerankerScore.Value:F2})");
                    }
                    else if (result.SearchScore.HasValue)
                    {
                        string scoreBar = GetScoreBar(result.SearchScore.Value);
                        formatted.AppendLine($"🎯 Score: {scoreBar} ({result.SearchScore.Value:F2})");
                    }

                    // Semantic captions with highlighting
                    if (result.SemanticCaptions.Any())
                    {
                        formatted.AppendLine($"\n💡 Key Insights:");
                        foreach (var caption in result.SemanticCaptions.Take(2))
                        {
                            formatted.AppendLine($"   ▸ {caption.Text}");
                        }
                    }

                    // Content preview
                    if (!string.IsNullOrEmpty(result.ContentPreview))
                    {
                        formatted.AppendLine($"\n📝 Preview:");
                        formatted.AppendLine($"   {TruncateText(result.ContentPreview, 200)}");
                    }

                    // Chunks with better formatting
                    if (result.Chunks.Any())
                    {
                        formatted.AppendLine($"\n📚 Content Sections ({result.TotalChunks} total):");
                        for (int i = 0; i < Math.Min(3, result.Chunks.Count); i++)
                        {
                            formatted.AppendLine($"   {i + 1}. {TruncateText(result.ChunkPreviews[i], 150)}");
                        }
                        if (result.TotalChunks > 3)
                            formatted.AppendLine($"   ⋯ and {result.TotalChunks - 3} more sections");
                    }

                    // Highlights
                    if (result.Highlights.Any())
                    {
                        formatted.AppendLine($"\n✨ Matched Terms:");
                        foreach (var highlight in result.Highlights.Take(2))
                        {
                            foreach (var value in highlight.Value.Take(2))
                            {
                                formatted.AppendLine($"   • {value}");
                            }
                        }
                    }



                    // fileName URL
                    if (!string.IsNullOrEmpty(result.FileName))
                        formatted.AppendLine($"\n🔗 Source: <a href=\"{result.FileName}\" target=\"_blank\">Source</a>");

                    formatted.AppendLine("\n═══════════════════════════════════════════════════════\n");
                }
            }

            return formatted.ToString();
        }

        // Helper method to generate star rating based on score
        private string GetStarRating(double score)
        {
            // Normalize score to 0-5 stars
            // Adjust thresholds based on your scoring system
            int stars;

            if (score >= 3.5) stars = 5;
            else if (score >= 2.8) stars = 4;
            else if (score >= 2.0) stars = 3;
            else if (score >= 1.2) stars = 2;
            else stars = 1;

            return new string('⭐', stars);
        }

        // Helper method to create visual score bar      
        
        private string GetScoreBar(double score)
        {
            // Clamp 0–3
            double clamped = Math.Min(Math.Max(score, 0), 3);
            double percentage = (clamped / 3.0) * 100;
            int filled = (int)(percentage / 10);
            int empty = 10 - filled;

            return $"{RepeatString("█", filled)}{RepeatString("░", empty)} {percentage:0}%";
        }



        // Helper method to truncate text with ellipsis
        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength).TrimEnd() + "...";
        }



        // Helper method to repeat a string
        private string RepeatString(string text, int count)
        {
            if (count <= 0) return string.Empty;
            return string.Concat(Enumerable.Repeat(text, count));
        }

        // Helper classes
        private class SentimentJsonResponse
        {
            public string OverallSentiment { get; set; }

            public ConfidenceScores ConfidenceScores { get; set; }

            public double PositiveScore => ConfidenceScores?.Positive ?? 0.0;
            public double NeutralScore => ConfidenceScores?.Neutral ?? 0.0;
            public double NegativeScore => ConfidenceScores?.Negative ?? 0.0;

            public string MatchWithJobDescription { get; set; }

            public bool IsTailored { get; set; }

            public string Reasoning { get; set; }

            public List<RequirementModel> Requirements { get; set; }

        }

       

        private class RequirementModel
        {
            public string Requirement { get; set; }
            public bool? IsMatched { get; set; }
            public string Evidence { get; set; }
        }

        private class SentimentResult
        {
            public string Sentiment { get; set; }
            public double PositiveScore { get; set; }
            public double NeutralScore { get; set; }
            public double NegativeScore { get; set; }
            public string MatchWithJobDescription { get; set; }
            public bool IsTailored { get; set; }
            public string Reasoning { get; set; }
            public List<RequirementModel> Requirements { get; set; }
        }

    }
}
