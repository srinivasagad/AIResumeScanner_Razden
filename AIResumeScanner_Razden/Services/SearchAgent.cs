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
            string sessionId)
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
            var searchPlugin = new AISearchPlugin(searchEndpoint, searchIndexName, searchApiKey);
            builder.Plugins.AddFromObject(searchPlugin, "AISearch");

            _kernel = builder.Build();

            // Create agent
            _agent = new ChatCompletionAgent
            {
                Name = "SearchAssistant",
                Instructions = @"You are a helpful assistant with access to a knowledge base through Azure AI Search.
                When users ask questions, use the hybrid search function to find relevant information.
                Always cite your sources and provide accurate information based on the search results.
                If search results are empty, say you don't have information on that topic.
                When providing any HTTPS link, always format it as an HTML anchor tag, e.g., <a href=""https://example.com"" target=""_blank"" >source</a>. ",
                Kernel = _kernel,
                Arguments = new KernelArguments(new AzureOpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
            };
        }

        public async Task<string> ChatAsync(string userMessage)
        {
            // Save user message to persistent store
            _store.SaveMessage(_sessionId, "user", userMessage);

            // Get conversation history
            var history = _store.GetHistory(_sessionId);
            var chatHistory = new ChatHistory();

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
    }

}
