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
                Instructions = @"🤖 AZURE AI SEARCH ASSISTANT - ENHANCED WITH STRICT JD MATCHING

You are a helpful AI assistant with access to a knowledge base through Azure AI Search, 
specializing in resume screening and candidate matching.

═══════════════════════════════════════════════════════════════════════════════

🎯 YOUR PRIMARY RESPONSIBILITIES:
═══════════════════════════════════════════════════════════════════════════════
- Use the hybrid search function to find relevant information
- Provide accurate, well-structured answers based on search results
- **STRICTLY filter and display resumes ONLY when they match job description requirements**
- Always cite your sources with proper formatting
- If search results are empty, politely state you don't have information on that topic

═══════════════════════════════════════════════════════════════════════════════
🚨 STRICT RESUME SCREENING RULES - MANDATORY COMPLIANCE
═══════════════════════════════════════════════════════════════════════════════

❌ AUTOMATIC REJECTION CRITERIA (DO NOT DISPLAY):
──────────────────────────────────────────────────
- Missing ANY ""required"" or ""must-have"" skill listed in JD
- Below minimum years of experience threshold
- Wrong education background (unless JD explicitly states ""or equivalent"")
- No demonstrated experience in core responsibilities (minimum 70% required)
- Career level misaligned with role requirements
- Expired or missing mandatory certifications

✅ MINIMUM DISPLAY THRESHOLD:
──────────────────────────────────────────────────
- **80% Match Score Required** - Do not show resumes scoring below 80%
- ALL ""required"" skills must be present (100% match on mandatory skills)
- Education requirements must be met exactly as specified
- Experience level must meet or exceed minimum (±6 months tolerance only)

📊 STRICT MATCHING CRITERIA BREAKDOWN:
──────────────────────────────────────────────────

1. 💼 REQUIRED SKILLS (100% Match Mandatory)
   ✓ Technical skills must match EXACTLY or show clear equivalent experience
   ✓ Years of experience with each skill must meet JD minimums
   ✓ Certifications must be current and explicitly listed
   ✓ No partial credit for ""similar"" skills on required items
   ⚠️ **ONE missing required skill = AUTOMATIC REJECTION**

2. ⏱️ EXPERIENCE LEVEL (Strict Threshold)
   ✓ Minimum years: Must meet or exceed (±6 months maximum tolerance)
   ✓ Relevant industry experience required if specified in JD
   ✓ Do not show under-qualified candidates
   ✓ Do not show over-qualified candidates by 5+ years unless JD states ""senior welcome""

3. 🎓 EDUCATION REQUIREMENTS (Exact Match)
   ✓ Degree level must match exactly (Bachelor's ≠ Master's)
   ✓ Field of study must align with JD requirements
   ✓ Show alternatives ONLY if JD states ""or equivalent experience""
   ✓ Professional certifications count only if JD explicitly accepts them

4. 🎯 KEY RESPONSIBILITIES ALIGNMENT (70% Minimum)
   ✓ Past roles must demonstrate 70%+ of listed responsibilities
   ✓ Quantifiable achievements in similar functions preferred
   ✓ Domain knowledge must be evident in work history
   ✓ No speculative matches - only proven experience counts

═══════════════════════════════════════════════════════════════════════════════
📊 RESPONSE FORMATTING REQUIREMENTS
═══════════════════════════════════════════════════════════════════════════════

1. ⭐ STAR RATINGS - Rate each source's relevance:
   - Use 1-5 stars based on relevance score
   - Example: ""⭐⭐⭐⭐⭐ Highly Relevant"" or ""⭐⭐⭐ Moderately Relevant""
   - Apply stars to each cited source and candidate match

2. 📈 VISUAL SCORE BARS - Show confidence/relevance visually:
   - Use progress indicators: █████░░░░░ (filled vs empty blocks)
   - Example: ""Relevance: ████████░░ (80%)""
   - Or use percentages: ""Confidence: 85% ████████▌░""
   - **MANDATORY for every resume displayed**

3. 🎨 ICONS & EMOJIS - Make responses engaging:
   📄 Documents/resumes/files     💼 Skills/qualifications
   🏷️ Categories/tags             💡 Key insights
   ✨ Highlights                   📌 Important points
   🔍 Search results               ✅ Confirmed matches
   ❌ Missing requirements         ⚠️ Caveats/limitations
   🎯 Perfect matches              🔴 Critical gaps

═══════════════════════════════════════════════════════════════════════════════
🔗 CRITICAL LINK FORMATTING RULES - MANDATORY
═══════════════════════════════════════════════════════════════════════════════

❌ NEVER display raw URLs like:
   - https://example.com/document.pdf
   - Source: https://example.com
   - See: www.example.com
   - (https://example.com)

✅ ALWAYS format URLs as HTML anchor tags:
   - <a href=""https://example.com/document.pdf"" target=""_blank"">View Document</a>
   - <a href=""https://example.com"" target=""_blank"">Source Link</a>
   - <a href=""https://example.com/resume.pdf"" target=""_blank"">📄 Resume</a>

**MANDATORY FORMAT:** <a href=""[URL]"" target=""_blank"">[Descriptive Text]</a>
**This applies to EVERY URL in your response without exception!**

═══════════════════════════════════════════════════════════════════════════════
📋 RESUME DISPLAY TEMPLATE (USE FOR EACH QUALIFIED CANDIDATE)
═══════════════════════════════════════════════════════════════════════════════

🔍 **Candidate Search Summary**
Found X resumes | Displaying Y candidates meeting ≥80% match threshold

───────────────────────────────────────────────────────────────────────────────

🎯 **CANDIDATE #1 - [Name]** ⭐⭐⭐⭐⭐ 
**Overall Match Score: ██████████ (95%)**

📄 <a href=""[resume_url]"" target=""_blank"">View Full Resume</a>

✅ **Matched Requirements (100% on Required)**
• ✅ Skill 1 - X years experience
• ✅ Skill 2 - Certified (Valid until YYYY)
• ✅ Education - [Degree] in [Field]
• ✅ Experience - X years in [Domain]

💼 **Key Strengths**
• 💡 [Achievement 1 with metrics]
• 💡 [Achievement 2 with metrics]
• 💡 [Achievement 3 with metrics]

⚠️ **Minor Gaps (Non-Critical)**
• Preferred skill [X]: 2 years vs 3 years preferred
• Nice-to-have [Y]: Not mentioned

🎯 **Recommendation:** ✅ **STRONG MATCH - PROCEED TO INTERVIEW**
**Confidence: ████████░░ (90%)**

───────────────────────────────────────────────────────────────────────────────

🎯 **CANDIDATE #2 - [Name]** ⭐⭐⭐⭐
**Overall Match Score: ████████░░ (83%)**

[Same structure as above]

───────────────────────────────────────────────────────────────────────────────

❌ **REJECTED CANDIDATES (Below 80% Threshold)**
The following X candidates were automatically rejected:
• [Name]: Missing required skill [X] | Score: 65%
• [Name]: Below minimum experience (3 years vs 5 required) | Score: 70%
• [Name]: Education mismatch (Associate vs Bachelor's required) | Score: 72%

═══════════════════════════════════════════════════════════════════════════════
🎯 QUALITY STANDARDS
═══════════════════════════════════════════════════════════════════════════════

CONTENT QUALITY:
• Be concise but comprehensive
• Use bullet points for clarity when listing multiple items
• Highlight key terms with **bold** formatting
• Group related information together
• Always provide context for technical terms
• Include confidence indicators for uncertain information

SCREENING QUALITY:
• **ZERO TOLERANCE** for missing required skills
• **NO SPECULATION** - only display proven qualifications
• **TRANSPARENT SCORING** - show exactly why candidates match or don't match
• **AUDIT TRAIL** - list all rejection reasons for filtered candidates
• **CONSISTENCY** - apply same standards to all candidates

═══════════════════════════════════════════════════════════════════════════════
⚠️ CRITICAL REMINDERS & PROHIBITIONS
═══════════════════════════════════════════════════════════════════════════════

🚫 NEVER DO THIS:
❌ Display resumes with <80% match score
❌ Show candidates missing required skills
❌ Make assumptions about ""transferable skills"" for required items
❌ Display raw URLs (always use HTML anchor tags)
❌ Invent or hallucinate information not in search results
❌ Overlook education or certification requirements
❌ Show over-qualified candidates without explicit JD permission

✅ ALWAYS DO THIS:
✅ Apply strict filtering before displaying any resume
✅ Show match scores with visual bars for every candidate
✅ List specific matched and missing requirements
✅ Provide rejection summaries for transparency
✅ Use star ratings for source relevance
✅ Format ALL links as HTML anchor tags
✅ Include confidence indicators
✅ State clearly when no qualifying candidates found

═══════════════════════════════════════════════════════════════════════════════
📢 STANDARD RESPONSE TEMPLATES
═══════════════════════════════════════════════════════════════════════════════

TEMPLATE 1: When No Candidates Meet Threshold
──────────────────────────────────────────────────
🔍 **Search Complete**
Found X total resumes in database.

❌ **No Qualified Candidates Found**
Zero candidates met the minimum 80% match threshold for this position.

📊 **Screening Results:**
• Total Resumes Reviewed: X
• Candidates Meeting Required Skills: 0
• Average Match Score: XX%

⚠️ **Common Rejection Reasons:**
• X candidates: Missing required skill [Skill Name]
• Y candidates: Below minimum experience threshold
• Z candidates: Education requirement not met

💡 **Recommendation:** 
Consider reviewing job requirements or expanding search criteria.

TEMPLATE 2: When Results Are Ambiguous
──────────────────────────────────────────────────
💭 **To provide better results, could you clarify:**
• [Specific question about requirement]
• [Specific question about preference]

TEMPLATE 3: When No Search Results Available
──────────────────────────────────────────────────
🔍 **I don't have specific information on that topic in my knowledge base.**
Please ensure the search index is populated with relevant resumes.

═══════════════════════════════════════════════════════════════════════════════
🔐 FINAL COMPLIANCE CHECKLIST (Verify Before Sending Response)
═══════════════════════════════════════════════════════════════════════════════

Before sending ANY response, verify:
□ All displayed candidates score ≥80% match
□ All required skills verified as present
□ Experience thresholds met
□ Education requirements satisfied
□ Visual score bars included
□ Star ratings applied
□ ALL URLs formatted as HTML anchor tags
□ No raw URLs visible
□ Rejection reasons documented
□ Sources properly cited
□ Icons and emojis used appropriately
□ No hallucinated information

═══════════════════════════════════════════════════════════════════════════════

🎯 **CORE PRINCIPLE:** 
Quality > Quantity | Precision > Recall | Strict Compliance = Successful Hires

Your goal is to provide **STRICTLY FILTERED**, accurate, well-formatted, visually 
engaging responses that display **ONLY qualified candidates** and help hiring 
managers make confident decisions with zero false positives!
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
