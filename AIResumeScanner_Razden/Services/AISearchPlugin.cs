using Azure;
using Azure.AI.OpenAI;
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

namespace AIResumeScanner_Razden.Services
{
    public class AISearchPlugin
    {

        private readonly SearchClient _searchClient;
        private readonly string _embeddingModel;

        public AISearchPlugin(string endpoint, string indexName, string apiKey, string embeddingModel = "text-embedding-ada-002")
        {
            _searchClient = new SearchClient(
                new Uri(endpoint),
                indexName,
                new AzureKeyCredential(apiKey)
            );
            _embeddingModel = embeddingModel;
        }

        [KernelFunction, Description("Perform semantic search using vector embeddings")]
        public async Task<string> SemanticSearch(
            [Description("The search query")] string query,
            [Description("Number of results")] int top = 3)
        {
            try
            {
                var queryEmbedding = await GenerateEmbedding(query);
                // For production, you'd generate embeddings using Azure OpenAI
                // This is a simplified example
                var searchOptions = new SearchOptions
                {
                    Size = top,
                    Select = { "Id", "Title", "chunks", "Content", "fileName" },
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


            }
            catch (Exception ex)
            {
                return $"Search error: {ex.Message}";
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

        private string FormatSearchResults1(List<SearchResultModel> results, long totalCount)
        {
            var formatted = new StringBuilder();
            formatted.AppendLine($"Found {totalCount} results:\n");

            foreach (var result in results)
            {
                formatted.AppendLine($"Rank #{result.Rank} - Result {result.Rank}:");

                if (!string.IsNullOrEmpty(result.Title))
                    formatted.AppendLine($"  Title: {result.Title}");

                if (!string.IsNullOrEmpty(result.FileName))
                    //formatted.AppendLine($"  File Name: {result.FileName}");
                    formatted.AppendLine($"  FileName Url: <a href='{result.FileName}' >Source</a>");

                if (!string.IsNullOrEmpty(result.Category))
                    formatted.AppendLine($"  Category: {result.Category}");

                if (result.Skills.Any())
                    formatted.AppendLine($"  Skills: {string.Join(", ", result.Skills)}");

                if (!string.IsNullOrEmpty(result.ContentPreview))
                    formatted.AppendLine($"  Content: {result.ContentPreview}");

                if (result.Chunks.Any())
                {
                    formatted.AppendLine($"  Chunks ({result.TotalChunks}):");
                    for (int i = 0; i < Math.Min(3, result.Chunks.Count); i++)
                    {
                        formatted.AppendLine($"    Chunk {i + 1}: {result.ChunkPreviews[i]}");
                    }
                    if (result.TotalChunks > 3)
                        formatted.AppendLine($"    ... and {result.TotalChunks - 3} more chunks");
                }

                if (result.SearchScore.HasValue)
                    formatted.AppendLine($"  Search Score: {result.SearchScore.Value:F4}");

                if (result.RerankerScore.HasValue)
                    formatted.AppendLine($"  Reranker Score: {result.RerankerScore.Value:F4} {result.ScoreLevel}");

                if (result.SemanticCaptions.Any())
                {
                    formatted.AppendLine($"\n💡 Semantic Captions:");
                    foreach (var caption in result.SemanticCaptions.Take(2))
                    {
                        formatted.AppendLine($"   • {caption.Text}");
                    }
                }

                if (result.Highlights.Any())
                {
                    formatted.AppendLine($"\n✨ Highlights:");
                    foreach (var highlight in result.Highlights.Take(2))
                    {
                        formatted.AppendLine($"   {highlight.Key}:");
                        foreach (var value in highlight.Value.Take(2))
                        {
                            formatted.AppendLine($"      - {value}");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(result.MetadataUrl))
                    formatted.AppendLine($"  Metadata Url: {result.MetadataUrl}");


                formatted.AppendLine();
            }

            return formatted.ToString();
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
        private string FormatSearchResults(SearchResults<Azure.Search.Documents.Models.SearchDocument> results)
        {
            var parsedResults = ParseSearchResults(results);
            return FormatSearchResults(parsedResults, results.TotalCount ?? 0);
        }

        private string FormatSearchResults(List<SearchResultModel> results, long totalCount)
        {
            var formatted = new StringBuilder();
            formatted.AppendLine($"🔍 Found {totalCount} relevant results\n");
            formatted.AppendLine("═══════════════════════════════════════════════════════\n");

            foreach (var result in results)
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
                    formatted.AppendLine($"🎯 Relevance: {scoreBar} ({result.RerankerScore.Value:F2})");
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
            // Create a visual bar (normalize to 0-10 scale)
            int normalizedScore = (int)Math.Min(Math.Max(score * 2.5, 0), 10);
            int filled = normalizedScore;
            int empty = 10 - filled;

            return $"{RepeatString("█", filled)}{RepeatString("░", empty)}";
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


    }
}
