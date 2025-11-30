using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.AI.OpenAI;
using System.Text.Json;


using Microsoft.AspNetCore.DataProtection.KeyManagement;

using Microsoft.Rest.Azure;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = OpenAI.Chat.ChatMessage;
using System.Net;

namespace AIResumeScanner_Razden.Services
{
    public class AzureAISearchGrounding
    {
        private readonly SearchClient _searchClient;
        private readonly AzureOpenAIClient _openAIClient;
        private readonly string _embeddingDeployment;
        private readonly string _chatDeployment;

        public AzureAISearchGrounding(
        string searchEndpoint,
        string searchApiKey,
        string indexName,
        string openAIEndpoint,
        string openAIApiKey,
        string embeddingDeployment = "text-embedding-ada-002",
        string chatDeployment = "gpt-5-nano")
        {
            _searchClient = new SearchClient(
                new Uri(searchEndpoint),
                indexName,
            new AzureKeyCredential(searchApiKey));

            _openAIClient = new AzureOpenAIClient(new Uri(openAIEndpoint), new AzureKeyCredential(openAIApiKey));

           

            _embeddingDeployment = embeddingDeployment;
            _chatDeployment = chatDeployment;
        }

        /// <summary>
        /// Performs hybrid search with grounding using both vector and keyword search
        /// </summary>
        public async Task<SearchResponse> QueryWithGrounding(
            string query,
            int topK = 25,
            bool includeGrounding = true)
        {
            try
            {
                // Step 1: Generate embedding for the query
                var queryEmbedding = await GenerateEmbedding(query);

                // Step 2: Perform hybrid search
                var searchOptions = new SearchOptions
                {
                    Size = topK,
                    Select = { "Id", "Title",  "experienceYears",  "Content", "chunks" },

                    // Enable semantic ranking if available
                    QueryType = SearchQueryType.Semantic,
                    SemanticSearch = new SemanticSearchOptions
                    {
                        SemanticConfigurationName = "semantic", // Configure in Azure portal
                        QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                        QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
                    }
                };

                // Add vector search
                if (queryEmbedding != null)
                {
                    searchOptions.VectorSearch = new VectorSearchOptions
                    {
                        Queries =
                    {
                        new VectorizedQuery(queryEmbedding)
                        {
                            KNearestNeighborsCount = topK,
                            Fields = { "chunkVectors" } // Your vector fields
                        }
                    }
                    };
                }

                // Perform the search
                SearchResults<SearchDocument> results = await _searchClient.SearchAsync<SearchDocument>(
                    query,
                    searchOptions);

                // Step 3: Parse results
                var profiles = new List<ProfileSearchResult>();
                await foreach (SearchResult<SearchDocument> result in results.GetResultsAsync())
                {
                    profiles.Add(new ProfileSearchResult
                    {
                        Id = GetFieldValue<int>(result.Document, "Id"),
                        //Name = GetFieldValue<string>(result.Document, "name"),
                        Title = GetFieldValue<string>(result.Document, "Title"),
                        //Category = GetFieldValue<string>(result.Document, "category"),
                        //Email = GetFieldValue<string>(result.Document, "email"),
                        //ExperienceYears = GetFieldValue<Int32>(result.Document, "experienceYears"),
                        //Experience = GetFieldValue<string>(result.Document, "experience"),
                        //Skills = GetFieldValue<List<string>>(result.Document, "skills") ?? new List<string>(),
                        //Summary = GetFieldValue<string>(result.Document, "summary"),
                        MatchScore = result.Score.HasValue ? result.Score.Value * 100 : 0,
                        SearchScore = result.Score ?? 0,
                        SemanticCaption = result.SemanticSearch?.Captions?.FirstOrDefault()?.Text
                    });
                }

                // Step 4: Generate grounding summary if requested
                string groundingSummary = null;
                if (includeGrounding && profiles.Any())
                {
                    groundingSummary = await GenerateGroundingSummary(query, profiles);
                }

                return new SearchResponse
                {
                    Query = query,
                    TotalResults = profiles.Count,
                    Profiles = profiles,
                    GroundingSummary = groundingSummary,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                return new SearchResponse
                {
                    Query = query,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Profiles = new List<ProfileSearchResult>()
                };
            }
        }

        /// <summary>
        /// Generates embeddings for text using Azure OpenAI
        /// </summary>
       


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

        /// <summary>
        /// Generates a grounded summary using GPT with search results as context
        /// </summary>
        private async Task<string> GenerateGroundingSummary(
            string query,
            List<ProfileSearchResult> profiles)
        {
            try
            {
                // Build context from top results
                var context = BuildContextFromProfiles(profiles.Take(10).ToList());

                string resumePrompt = "You are a helpful recruiter assistant. Analyze the candidate profiles and provide insights based on the search query. Be concise and factual.";
                string content = $"Query: {query}\n\n" +
                                 $"Candidate Profiles:\n{context}\n\n" +
                                 $"Provide a brief summary of the search results, highlighting key findings and top matching candidates.";

                var messages = new List<ChatMessage>
                                                        {
                                                           new SystemChatMessage(resumePrompt),
                                                           new UserChatMessage(content)
                                                        };

                // Create chat completion options
                var options = new ChatCompletionOptions
                {
                    Temperature = (float)0.7,
                    MaxOutputTokenCount = Convert.ToInt32("16000"),

                    TopP = (float)0.95,
                    FrequencyPenalty = (float)0,
                    PresencePenalty = (float)0
                };

                AzureKeyCredential credential = new AzureKeyCredential("BxUQYM8ND9UR2q3WqFrk2YlyHR4NHCG2ORy6xpublVSY4WIl3TwYJQQJ99BJACYeBjFXJ3w3AAABACOGeGiz");

                // Initialize the AzureOpenAIClient
                AzureOpenAIClient azureClient = new(new Uri("https://resumeembeddingendpoint.openai.azure.com/"), credential);
                // Initialize the ChatClient with the specified deployment name
                ChatClient chatClient = azureClient.GetChatClient("gpt-5-nano");
                // Create the chat completion request
                ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);
                string responseJson = "";
                if (completion != null)
                {
                    // Get the assistant's response content (the JSON string)
                    responseJson = completion.Content[0].Text.ToString();
                }

                return responseJson;


            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating grounding summary: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds context string from profile results for grounding
        /// </summary>
        private string BuildContextFromProfiles(List<ProfileSearchResult> profiles)
        {
            var contextBuilder = new System.Text.StringBuilder();

            foreach (var profile in profiles)
            {
                contextBuilder.AppendLine($"Name: {profile.Name}");
                contextBuilder.AppendLine($"Title: {profile.Title}");
                contextBuilder.AppendLine($"Experience: {profile.Experience}");
                contextBuilder.AppendLine($"Skills: {string.Join(", ", profile.Skills)}");
                contextBuilder.AppendLine($"Match Score: {profile.MatchScore:F1}%");

                if (!string.IsNullOrEmpty(profile.Summary))
                {
                    contextBuilder.AppendLine($"Summary: {profile.Summary}");
                }

                contextBuilder.AppendLine();
            }

            return contextBuilder.ToString();
        }

        /// <summary>
        /// Helper method to safely extract field values from search documents
        /// </summary>
        private T GetFieldValue<T>(SearchDocument document, string fieldName)
        {
            try
            {
                if (document.TryGetValue(fieldName, out object value))
                {
                    if (value is JsonElement jsonElement)
                    {
                        return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                    }
                    return (T)value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting field {fieldName}: {ex.Message}");
            }
            return default;
        }

        /// <summary>
        /// Alternative: Simple hybrid search without grounding
        /// </summary>
        public async Task<List<ProfileSearchResult>> SimpleHybridSearch(
            string query,
            int topK = 25)
        {
            var response = await QueryWithGrounding(query, topK, includeGrounding: false);
            return response.Profiles;
        }

    }
    // Response models
    public class SearchResponse
    {
        public string Query { get; set; }
        public int TotalResults { get; set; }
        public List<ProfileSearchResult> Profiles { get; set; }
        public string GroundingSummary { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class ProfileSearchResult
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string Email { get; set; }
        public int ExperienceYears { get; set; }
        public string Experience { get; set; }
        public List<string> Skills { get; set; }
        public string Summary { get; set; }
        public double MatchScore { get; set; }
        public double SearchScore { get; set; }
        public string SemanticCaption { get; set; }
    }
}
