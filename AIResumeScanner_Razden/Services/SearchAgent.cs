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
    public class SearchAgent
    {
        private readonly Kernel _kernel;
        private readonly ConversationStore _store;
        private readonly ChatCompletionAgent _agent;
        private readonly string _sessionId;
       

        public SearchAgent(
            string azureOpenAiEndpoint,
            string azureOpenAiApiKey,
            string chatDeploymentName,
            string embeddingDeploymentName,
            string searchEndpoint,
            string searchIndexName,
            string searchApiKey,
            string sessionId,
            string textAnalyticsEndpoint,
            string textAnalyticsApiKey)
        {
            _sessionId = sessionId;
            _store = new ConversationStore();

            // Build kernel with Azure OpenAI
            var builder = Kernel.CreateBuilder();
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: chatDeploymentName,
                endpoint: azureOpenAiEndpoint,
                apiKey: azureOpenAiApiKey
            );

            // Add AI Search plugin
            var searchPlugin = new AISearchPlugin(searchEndpoint, searchIndexName, searchApiKey, embeddingDeploymentName, textAnalyticsEndpoint, textAnalyticsApiKey);
            builder.Plugins.AddFromObject(searchPlugin, "AISearch");

            _kernel = builder.Build();

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

        public async Task<string> ChatAsync(string userMessage)
        {
            var chatHistory = new ChatHistory();
            /*
            // ✅ Check if same user message already exists in history
            if (UserPromptExists(_sessionId, userMessage))
            {
                
                _store.SaveMessage(_sessionId, "user", userMessage);
                // Optionally, return last assistant response for that same prompt
                var previousResponse = GetLastAssistantResponse(_sessionId, userMessage);
                if (previousResponse != null)
                {
                    _store.SaveMessage(_sessionId, "assistant", previousResponse);
                    return previousResponse;
                }
            }
            */


            // Save user message to persistent store
            _store.SaveMessage(_sessionId, "user", userMessage);

            // Get conversation history
            var history = _store.GetHistory(_sessionId);
           

            // Load previous messages
            foreach (var msg in history.TakeLast(10)) // Keep last 10 messages for context
            {
                if (msg.Role == "user")
                    chatHistory.AddUserMessage(msg.Content);
                else if (msg.Role == "assistant")
                    chatHistory.AddAssistantMessage(msg.Content);
            }

            // Get agent response
            chatHistory.AddUserMessage(userMessage);

            var response = "";
            await foreach (var message in _agent.InvokeAsync(chatHistory))
            {
                response = message.Message.Content ?? "";
            }

            // Save assistant response
            _store.SaveMessage(_sessionId, "assistant", response);
          

            return response;
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
            _store.SaveMessage(_sessionId, "user", userMessage);

            var history = _store.GetHistory(_sessionId);
            var chatHistory = new ChatHistory();

            foreach (var msg in history.SkipLast(1).TakeLast(10))
            {
                if (msg.Role == "user")
                    chatHistory.AddUserMessage(msg.Content);
                else if (msg.Role == "assistant")
                    chatHistory.AddAssistantMessage(msg.Content);
            }

            chatHistory.AddUserMessage(userMessage);

            var fullResponse = new System.Text.StringBuilder();

            await foreach (var message in _agent.InvokeAsync(chatHistory))
            {
                if (message.Message.Content != null)
                {
                    fullResponse.Append(message.Message.Content);

                    // Show function calls in real-time
                    Console.WriteLine($"\n[Debug - {message.Message.Role}]: {message.Message.Content}");
                }
            }

            var response = fullResponse.ToString();
            _store.SaveMessage(_sessionId, "assistant", response);

            return response;
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
                    // Return next message if it’s from assistant
                    if (messages[i + 1].Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                        return messages[i + 1].Content;
                }
            }

            return null;
        }

    }

}
