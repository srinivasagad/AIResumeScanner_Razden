using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using AIResumeScanner_Razden.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIResumeScanner_Razden.Models;
namespace AIResumeScanner_Razden.Services
{
    public class SearchAgent2
    {
        private readonly Kernel _kernel;
        private readonly ConversationStore _store;
        private readonly ChatCompletionAgent _agent;
        private readonly string _sessionId;
        private readonly TokenUsageService _tokenUsageService;
        private readonly string _chatDeploymentName;
        private readonly IChatCompletionService _chatService;

        public SearchAgent2(
            string azureOpenAiEndpoint,
            string azureOpenAiApiKey,
            string chatDeploymentName,
            string embeddingDeploymentName,
            string searchEndpoint,
            string searchIndexName,
            string searchApiKey,
            string sessionId,
            string textAnalyticsEndpoint,
            string textAnalyticsApiKey,
            TokenUsageService tokenUsageService = null) // Optional for backward compatibility
        {
            _sessionId = sessionId;
            _store = new ConversationStore();
            _tokenUsageService = tokenUsageService;
            _chatDeploymentName = chatDeploymentName;

            // Build kernel with Azure OpenAI
            var builder = Kernel.CreateBuilder();
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: chatDeploymentName,
                endpoint: azureOpenAiEndpoint,
                apiKey: azureOpenAiApiKey
            );

            // Add AI Search plugin - updated to pass token service
            var searchPlugin = new AISearchPlugin(searchEndpoint, searchIndexName, searchApiKey);
            builder.Plugins.AddFromObject(searchPlugin, "AISearch");

            _kernel = builder.Build();

            // Get chat completion service for token tracking
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();

            // Create agent
            _agent = new ChatCompletionAgent
            {
                Name = "SearchAssistant",
                Instructions = @"You are a helpful AI assistant with access to a knowledge base through Azure AI Search.

🎯 YOUR PRIMARY RESPONSIBILITIES:
- Use the hybrid search function to find relevant information
- Provide accurate, well-structured answers based on search results
- Always cite your sources with proper formatting
- If search results are empty, politely state you don't have information on that topic

📊 RESPONSE FORMATTING REQUIREMENTS:

1. ⭐ STAR RATINGS - Rate each source's relevance:
   - Use 1-5 stars based on relevance score
   - Example: ""⭐⭐⭐⭐⭐ Highly Relevant"" or ""⭐⭐⭐ Moderately Relevant""
   - Apply stars to each cited source

2. 📈 VISUAL SCORE BARS - Show confidence/relevance visually:
   - Use progress indicators: █████░░░░░ (filled vs empty blocks)
   - Example: ""Relevance: ████████░░ (80%)""
   - Or use percentages: ""Confidence: 85% ████████▌░""

3. 🎨 ICONS & EMOJIS - Make responses engaging:
   - 📄 For documents/files
   - 💼 For skills/qualifications
   - 🏷️ For categories/tags
   - 💡 For key insights
   - ✨ For highlights
   - 📌 For important points
   - 🔍 For search results
   - ✅ For confirmed information
   - ⚠️ For caveats or limitations

🔗 CRITICAL LINK FORMATTING RULES - MANDATORY:

❌ NEVER display raw URLs like:
   - https://example.com/document.pdf
   - Source: https://example.com
   - See: www.example.com
   - (https://example.com)

✅ ALWAYS format URLs as HTML anchor tags:
   - <a href=""https://example.com/document.pdf"" target=""_blank"">View Document</a>
   - <a href=""https://example.com"" target=""_blank"">Source Link</a>
   - <a href=""https://example.com/resume.pdf"" target=""_blank"">📄 Resume</a>

MANDATORY FORMAT: <a href=""[URL]"" target=""_blank"">[Descriptive Text]</a>

This applies to EVERY URL in your response without exception!

📋 RESPONSE STRUCTURE TEMPLATE:

When providing answers, structure them like this:

🔍 **Search Summary**
Found X relevant results matching your query.

💡 **Key Findings** ⭐⭐⭐⭐⭐
[Main answer with proper formatting]
Relevance: ████████░░ (80%)

📄 **Source 1** ⭐⭐⭐⭐⭐
[Information from source]
🔗 <a href=""https://source1.com"" target=""_blank"">View Source</a>

📄 **Source 2** ⭐⭐⭐⭐
[Information from source]
Confidence: ███████░░░ (70%)
🔗 <a href=""https://source2.com"" target=""_blank"">View Document</a>

✨ **Additional Context**
[Any supplementary information]

🎯 QUALITY STANDARDS:
- Be concise but comprehensive
- Use bullet points for clarity when listing multiple items
- Highlight key terms with **bold** formatting
- Group related information together
- Always provide context for technical terms
- Include confidence indicators for uncertain information

⚠️ IMPORTANT REMINDERS:
- If no relevant results found: ""🔍 I don't have specific information on that topic in my knowledge base.""
- For ambiguous queries: ""💭 To provide better results, could you clarify...""
- For multiple interpretations: Present top results with confidence scores
- Never invent or hallucinate information not in search results
- Always indicate source quality with star ratings


Remember: Your goal is to provide accurate, well-formatted, visually engaging responses that help users quickly understand the information and access sources easily!
                ",
                Kernel = _kernel,
                Arguments = new KernelArguments(new AzureOpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
            };
        }

        /// <summary>
        /// Check if the agent has enough tokens to make a request
        /// </summary>
        public bool CanMakeRequest(int estimatedTokens = 1000)
        {
            if (_tokenUsageService == null)
                return true; // No tracking, allow request

            return _tokenUsageService.GetRemainingTokensPerMinute() >= estimatedTokens;
        }

        /// <summary>
        /// Get estimated tokens for a message
        /// </summary>
        private int EstimateTokens(string text)
        {
            // Rough approximation: 1 token ≈ 4 characters
            // For more accurate counting, consider using tiktoken library
            return (int)Math.Ceiling(text.Length / 4.0);
        }

        public async Task<string> ChatAsync(string userMessage)
        {
            var startTime = DateTime.UtcNow;
            var chatHistory = new ChatHistory();

            // Estimate tokens for the request
            int estimatedPromptTokens = EstimateTokens(userMessage);

            // Check if we have enough tokens
            if (!CanMakeRequest(estimatedPromptTokens))
            {
                var errorMsg = "⚠️ Rate limit reached. Please wait before making another request.";

                _tokenUsageService?.RecordUsage(
                    operation: "Chat Request",
                    promptTokens: 0,
                    completionTokens: 0,
                    model: _chatDeploymentName,
                    success: false,
                    errorMessage: "Rate limit - insufficient tokens"
                );

                return errorMsg;
            }

            try
            {
                // Save user message to persistent store
                _store.SaveMessage(_sessionId, "user", userMessage);

                // Get conversation history
                var history = _store.GetHistory(_sessionId);

                // Load previous messages (include in token estimate)
                var contextMessages = history.TakeLast(10).ToList();
                foreach (var msg in contextMessages)
                {
                    if (msg.Role == "user")
                    {
                        chatHistory.AddUserMessage(msg.Content);
                        estimatedPromptTokens += EstimateTokens(msg.Content);
                    }
                    else if (msg.Role == "assistant")
                    {
                        chatHistory.AddAssistantMessage(msg.Content);
                        estimatedPromptTokens += EstimateTokens(msg.Content);
                    }
                }

                // Add current user message
                chatHistory.AddUserMessage(userMessage);

                // Get agent response with metadata
                var response = "";
                int actualPromptTokens = estimatedPromptTokens;
                int actualCompletionTokens = 0;

                await foreach (var message in _agent.InvokeAsync(chatHistory))
                {
                    response = message.Message.Content ?? "";

                    // Try to extract token usage from metadata if available
                    if (message.Message.Metadata != null &&
                        message.Message.Metadata.TryGetValue("Usage", out var usageObj))
                    {
                        // Azure OpenAI returns usage information
                        //var usage = usageObj as Microsoft.SemanticKernel.ChatCompletion.ChatModelUsage;
                        //if (usage != null)
                        //{
                        //    actualPromptTokens = usage.InputTokenCount ?? estimatedPromptTokens;
                        //    actualCompletionTokens = usage.OutputTokenCount ?? EstimateTokens(response);
                        //}
                    }
                }

                // If we couldn't get actual tokens from metadata, estimate
                if (actualCompletionTokens == 0)
                {
                    actualCompletionTokens = EstimateTokens(response);
                }

                // Save assistant response
                _store.SaveMessage(_sessionId, "assistant", response);

                // Calculate duration
                var duration = DateTime.UtcNow - startTime;

                // Record successful token usage
                _tokenUsageService?.RecordUsage(
                    operation: "Chat Request",
                    promptTokens: actualPromptTokens,
                    completionTokens: actualCompletionTokens,
                    model: _chatDeploymentName,
                    success: true,
                    sessionId: _sessionId,
                    userQuery: userMessage,
                    durationSeconds: duration.TotalSeconds
                );

                Console.WriteLine($"[Token Usage] Prompt: {actualPromptTokens}, Completion: {actualCompletionTokens}, Total: {actualPromptTokens + actualCompletionTokens}, Duration: {duration.TotalSeconds:F2}s");

                return response;
            }
            catch (Exception ex)
            {
                // Record failed attempt
                _tokenUsageService?.RecordUsage(
                    operation: "Chat Request",
                    promptTokens: estimatedPromptTokens,
                    completionTokens: 0,
                    model: _chatDeploymentName,
                    success: false,
                    errorMessage: ex.Message
                );

                Console.WriteLine($"[Error] Token tracking recorded failure: {ex.Message}");
                throw;
            }
        }

        public List<Models.ChatMessage> GetConversationHistory()
        {
            return _store.GetHistory(_sessionId);
        }

        public void ClearConversationHistory()
        {
            _store.ClearHistory(_sessionId);
        }

        // Alternative method with streaming and function call visibility
        public async Task<string> ChatWithStreamingAsync(string userMessage)
        {
            var startTime = DateTime.UtcNow;
            int estimatedPromptTokens = EstimateTokens(userMessage);

            // Check rate limits
            if (!CanMakeRequest(estimatedPromptTokens))
            {
                var errorMsg = "⚠️ Rate limit reached. Please wait before making another request.";

                _tokenUsageService?.RecordUsage(
                    operation: "Chat Request (Streaming)",
                    promptTokens: 0,
                    completionTokens: 0,
                    model: _chatDeploymentName,
                    success: false,
                    errorMessage: "Rate limit - insufficient tokens"
                );

                return errorMsg;
            }

            try
            {
                _store.SaveMessage(_sessionId, "user", userMessage);

                var history = _store.GetHistory(_sessionId);
                var chatHistory = new ChatHistory();

                // Include context in token estimate
                var contextMessages = history.SkipLast(1).TakeLast(10).ToList();
                foreach (var msg in contextMessages)
                {
                    if (msg.Role == "user")
                    {
                        chatHistory.AddUserMessage(msg.Content);
                        estimatedPromptTokens += EstimateTokens(msg.Content);
                    }
                    else if (msg.Role == "assistant")
                    {
                        chatHistory.AddAssistantMessage(msg.Content);
                        estimatedPromptTokens += EstimateTokens(msg.Content);
                    }
                }

                chatHistory.AddUserMessage(userMessage);

                var fullResponse = new StringBuilder();
                int actualPromptTokens = estimatedPromptTokens;
                int actualCompletionTokens = 0;

                await foreach (var message in _agent.InvokeAsync(chatHistory))
                {
                    if (message.Message.Content != null)
                    {
                        fullResponse.Append(message.Message.Content);

                        // Show function calls in real-time
                        Console.WriteLine($"\n[Debug - {message.Message.Role}]: {message.Message.Content}");

                        // Try to extract token usage
                        if (message.Message.Metadata != null &&
                            message.Message.Metadata.TryGetValue("Usage", out var usageObj))
                        {
                            //var usage = usageObj as Microsoft.SemanticKernel.ChatCompletion.ChatModelUsage;
                            //if (usage != null)
                            //{
                            //    actualPromptTokens = usage.InputTokenCount ?? estimatedPromptTokens;
                            //    actualCompletionTokens = usage.OutputTokenCount ?? 0;
                            //}
                        }
                    }
                }

                var response = fullResponse.ToString();

                // Estimate completion tokens if not available from metadata
                if (actualCompletionTokens == 0)
                {
                    actualCompletionTokens = EstimateTokens(response);
                }

                _store.SaveMessage(_sessionId, "assistant", response);

                // Calculate duration
                var duration = DateTime.UtcNow - startTime;

                // Record token usage
                _tokenUsageService?.RecordUsage(
                    operation: "Chat Request (Streaming)",
                    promptTokens: actualPromptTokens,
                    completionTokens: actualCompletionTokens,
                    model: _chatDeploymentName,
                    success: true
                );

                Console.WriteLine($"[Token Usage - Streaming] Prompt: {actualPromptTokens}, Completion: {actualCompletionTokens}, Total: {actualPromptTokens + actualCompletionTokens}, Duration: {duration.TotalSeconds:F2}s");

                return response;
            }
            catch (Exception ex)
            {
                // Record failed attempt
                _tokenUsageService?.RecordUsage(
                    operation: "Chat Request (Streaming)",
                    promptTokens: estimatedPromptTokens,
                    completionTokens: 0,
                    model: _chatDeploymentName,
                    success: false,
                    errorMessage: ex.Message
                );

                Console.WriteLine($"[Error] Token tracking recorded failure: {ex.Message}");
                throw;
            }
        }

        public bool UserPromptExists(string sessionId, string prompt)
        {
            var state = _store.GetOrCreate(sessionId);

            // Case-insensitive, trimmed comparison
            return state.Messages.Any(m =>
                m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.Content.Trim(), prompt.Trim(), StringComparison.OrdinalIgnoreCase)
            );
        }

        public string? GetLastAssistantResponse(string sessionId, string prompt)
        {
            var state = _store.GetOrCreate(sessionId);
            var messages = state.Messages;

            for (int i = 0; i < messages.Count - 1; i++)
            {
                if (messages[i].Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(messages[i].Content.Trim(), prompt.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    // Return next message if it's from assistant
                    if (messages[i + 1].Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                        return messages[i + 1].Content;
                }
            }

            return null;
        }

        /// <summary>
        /// Get token usage statistics for this session
        /// </summary>
        public TokenSessionStats GetSessionStats()
        {
            if (_tokenUsageService == null)
                return new TokenSessionStats();

            var recentUsage = _tokenUsageService.GetRecentUsage(100);
            var sessionUsage = recentUsage.Where(r => r.Timestamp >= DateTime.UtcNow.AddHours(-1)).ToList();

            return new TokenSessionStats
            {
                SessionId = _sessionId,
                TotalTokens = sessionUsage.Sum(r => r.TotalTokens),
                TotalPromptTokens = sessionUsage.Sum(r => r.PromptTokens),
                TotalCompletionTokens = sessionUsage.Sum(r => r.CompletionTokens),
                RequestCount = sessionUsage.Count,
                SuccessfulRequests = sessionUsage.Count(r => r.Success),
                FailedRequests = sessionUsage.Count(r => !r.Success),
                AverageTokensPerRequest = sessionUsage.Any() ? sessionUsage.Average(r => r.TotalTokens) : 0
            };
        }

        public class TokenSessionStats
        {
            public string SessionId { get; set; }
            public int TotalTokens { get; set; }
            public int TotalPromptTokens { get; set; }
            public int TotalCompletionTokens { get; set; }
            public int RequestCount { get; set; }
            public int SuccessfulRequests { get; set; }
            public int FailedRequests { get; set; }
            public double AverageTokensPerRequest { get; set; }
        }
    }
}
