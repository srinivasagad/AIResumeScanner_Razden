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
using DocumentFormat.OpenXml.Wordprocessing;

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
            bool includeGrounding = true, string categoryFilter = null,
                                          string titleFilter = null,
                                          string skillFilter = null, SkillSearchMode skillSearchMode = SkillSearchMode.StartsWith)
        {
            try
            {
                // Step 1: Generate embedding for the query
                var queryEmbedding = await GenerateEmbedding(query);

                // Step 2: Perform hybrid search
                var searchOptions = new SearchOptions
                {
                    Size = topK,
                    Select = { "Id", "Name", "Email", "Title", "Category", "ExperienceYears", "Skills",  "Content", "chunks" },

                    // Enable semantic ranking if available
                    QueryType = SearchQueryType.Semantic,
                    SemanticSearch = new SemanticSearchOptions
                    {
                        SemanticConfigurationName = "semantic", // Configure in Azure portal
                        QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                        QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
                    }
                };

                // **Build filter expression**
                /*
                var filters = new List<string>();

                if (!string.IsNullOrWhiteSpace(categoryFilter))
                {
                    filters.Add($"Category eq '{categoryFilter.Replace("'", "''")}'");
                }

                if (!string.IsNullOrWhiteSpace(titleFilter))
                {
                    filters.Add($"Title eq '{titleFilter.Replace("'", "''")}'");
                }

                if (!string.IsNullOrWhiteSpace(skillFilter))
                {
                    // For collection fields, use any/all lambda expressions
                    filters.Add($"Skills/any(s: s eq '{skillFilter.Replace("'", "''")}'  or contains(s, '{skillFilter.Replace("'", "''")}'))");
                }

                // Apply combined filter
                if (filters.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filters);
                }
                */


                /////
                // Build filter expression
                var filters = BuildFilterExpression(categoryFilter, titleFilter, skillFilter, skillSearchMode);

                // Apply combined filter
                if (filters.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filters);
                    Console.WriteLine($"Applied Filter: {searchOptions.Filter}"); // Debug
                }



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
                        Id = GetFieldValue<string>(result.Document, "Id"),
                        Name = GetFieldValue<string>(result.Document, "Name"),
                        Title = GetFieldValue<string>(result.Document, "Title"),
                        Category = GetFieldValue<string>(result.Document, "Category"),
                        Email = GetFieldValue<string>(result.Document, "Email"),
                        ExperienceYears = GetFieldValue<double>(result.Document, "ExperienceYears"),
                        Experience = $"{GetFieldValue<double>(result.Document, "ExperienceYears")} years",
                        Skills = GetFieldValueForString<List<string>>(result.Document, "Skills") ?? new List<string>(),
                        Summary = GetFieldValue<string>(result.Document, "Content"),
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
        /// Builds filter expressions with multiple skill search strategies
        /// </summary>
        private List<string> BuildFilterExpression(
            string categoryFilter,
            string titleFilter,
            string skillFilter,
            SkillSearchMode skillSearchMode)
        {
            var filters = new List<string>();

            // Category filter
            if (!string.IsNullOrWhiteSpace(categoryFilter))
            {
                filters.Add($"Category eq '{EscapeODataString(categoryFilter)}'");
            }

            // Title filter
            if (!string.IsNullOrWhiteSpace(titleFilter))
            {
                filters.Add($"Title eq '{EscapeODataString(titleFilter)}'");
            }

            // Enhanced Skill filter with multiple modes
            if (!string.IsNullOrWhiteSpace(skillFilter))
            {
                var skillFilterExpression = BuildSkillFilterExpression(skillFilter, skillSearchMode);
                if (!string.IsNullOrEmpty(skillFilterExpression))
                {
                    filters.Add(skillFilterExpression);
                }
            }

            return filters;
        }


        /// <summary>
        /// Builds skill filter expression based on search mode
        /// </summary>
        private string BuildSkillFilterExpression(string skillFilter, SkillSearchMode mode)
        {
            var escapedSkill = EscapeODataString(skillFilter.Trim());

            switch (mode)
            {
                case SkillSearchMode.Exact:
                    // Exact match only (case-insensitive in Azure Search)
                    return $"Skills/any(s: s eq '{escapedSkill}')";

                case SkillSearchMode.Contains:
                    // Partial match - searches for skill within the text
                    return $"Skills/any(s: search.ismatch('{escapedSkill}', 's'))";

                case SkillSearchMode.StartsWith:
                    // Starts with (requires wildcard support)
                    return $"Skills/any(s: startswith(s, '{escapedSkill}'))";

                case SkillSearchMode.ExactOrContains:
                    // Combination of exact OR contains (default in your code)
                    return $"Skills/any(s: s eq '{escapedSkill}' or search.ismatch('{escapedSkill}', 's'))";

                case SkillSearchMode.Multiple:
                    // Search for multiple skills (comma or space separated)
                    return BuildMultipleSkillFilter(skillFilter);

                case SkillSearchMode.Fuzzy:
                    // Fuzzy matching for typos
                    return $"Skills/any(s: search.ismatch('{escapedSkill}~', 's'))";

                default:
                    return $"Skills/any(s: s eq '{escapedSkill}' or search.ismatch('{escapedSkill}', 's'))";
            }
        }

        /// <summary>
        /// Builds filter for multiple skills (OR logic)
        /// </summary>
        private string BuildMultipleSkillFilter(string skillFilter)
        {
            // Split by comma, semicolon, or pipe
            var skills = skillFilter.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (!skills.Any())
                return string.Empty;

            // Build OR condition for each skill
            var skillConditions = skills.Select(skill =>
                $"(s eq '{EscapeODataString(skill)}' or search.ismatch('{EscapeODataString(skill)}', 's'))"
            );

            return $"Skills/any(s: {string.Join(" or ", skillConditions)})";
        }

        /// <summary>
        /// Escapes special characters in OData strings
        /// </summary>
        private string EscapeODataString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // Replace single quotes with double single quotes for OData
            return value.Replace("'", "''");
        }


        /// <summary>
        /// Enum for different skill search modes
        /// </summary>
        public enum SkillSearchMode
        {
            /// <summary>Exact match only (case-insensitive)</summary>
            Exact,

            /// <summary>Partial match using contains</summary>
            Contains,

            /// <summary>Starts with the search term</summary>
            StartsWith,

            /// <summary>Exact OR contains (default)</summary>
            ExactOrContains,

            /// <summary>Search for multiple skills (OR logic)</summary>
            Multiple,

            /// <summary>Fuzzy search for typos</summary>
            Fuzzy
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
                var options = new ChatCompletionOptions()
                {
                    Temperature = (float)1,
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

        private T GetFieldValueForString<T>(SearchDocument document, string fieldName)
        {
            try
            {
                if (document.TryGetValue(fieldName, out object value))
                {
                    if (value is JsonElement jsonElement)
                    {
                        return HandleJsonElement<T>(jsonElement);
                    }

                    // Direct string to List<string> conversion
                    if (typeof(T) == typeof(List<string>) && value is string stringValue)
                    {
                        return (T)(object)ConvertToStringList(stringValue);
                    }

                    // Handle IEnumerable collections
                    if (typeof(T) == typeof(List<string>) && value is IEnumerable<object> enumerable)
                    {
                        var list = enumerable.Select(x => x?.ToString()).ToList();
                        return (T)(object)list;
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

        private T HandleJsonElement<T>(JsonElement jsonElement)
        {
            // Handle JSON arrays
            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                if (typeof(T) == typeof(List<string>))
                {
                    var list = new List<string>();
                    foreach (var element in jsonElement.EnumerateArray())
                    {
                        list.Add(element.GetString());
                    }
                    return (T)(object)list;
                }
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }

            // Handle comma-separated string in JSON
            if (jsonElement.ValueKind == JsonValueKind.String && typeof(T) == typeof(List<string>))
            {
                var stringValue = jsonElement.GetString();
                return (T)(object)ConvertToStringList(stringValue);
            }

            // Default JSON deserialization
            return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
        }

        private List<string> ConvertToStringList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<string>();

            return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
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
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string Email { get; set; }
        public double ExperienceYears { get; set; }
        public string Experience { get; set; }
        public List<string> Skills { get; set; }
        public string Summary { get; set; }
        public double MatchScore { get; set; }
        public double SearchScore { get; set; }
        public string SemanticCaption { get; set; }
    }
}
