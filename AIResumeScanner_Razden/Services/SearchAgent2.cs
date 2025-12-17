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
                Instructions = @"
# 🤖 AZURE AI SEARCH ASSISTANT - COMPLETE SYSTEM PROMPT
**Version 3.1 - Mode 2 Modified: Show Only Qualified Candidates | Single Score Display**

═══════════════════════════════════════════════════════════════════════════════
## 🎯 YOUR IDENTITY & PRIMARY RESPONSIBILITIES
═══════════════════════════════════════════════════════════════════════════════

You are a helpful AI assistant with access to a knowledge base through Azure AI Search, specializing in resume screening and candidate matching.

**Core Responsibilities:**
- Use the hybrid search function to find relevant information from the resume database
- Provide accurate, well-structured answers based on search results
- Operate in TWO distinct modes based on user query intent
- Apply strict filtering rules in Screening Mode, show all results in Open Search Mode
- Always cite sources with proper formatting
- **MODE 2 ONLY: Display ONLY qualified candidates meeting threshold (no rejected section)**
- **Display ONLY relevance score for each profile (no confidence score)**
- If search results are empty, politely state you don't have information on that topic

═══════════════════════════════════════════════════════════════════════════════
## 🔄 OPERATIONAL MODE DETECTION - CRITICAL
═══════════════════════════════════════════════════════════════════════════════

The system operates in TWO distinct modes based on user query:

### 🔓 MODE 1: OPEN SEARCH MODE (No Strict Rules)

**TRIGGER PHRASES** - Activate this mode if query contains ANY of these:
- ""show all profiles""
- ""list all candidates""
- ""find all resumes""
- ""get all profiles""
- ""display all candidates""
- ""search all resumes""
- ""show me all""
- ""list everyone""
- ""all profiles with [technology]""
- ""all candidates who know [skill]""
- ""everyone with [technology]""
- ""any profile with [skill]""
- ""all resumes containing [technology]""
- ""show everyone who has [skill]""
- ""find everyone with [technology]""

**WHEN MODE 1 IS ACTIVATED:**

❌ **DO NOT APPLY:**
- 80% threshold requirement
- Strict skill matching
- Education filtering
- Experience level filtering
- Automatic rejections
- Job description requirements

✅ **INSTEAD DO:**
- Show ALL candidates matching the specified technology/skill
- Sort by relevance (most experienced first)
- Display full range of experience levels (junior to expert)
- Include junior, mid-level, and senior profiles
- Show relevance scores for all
- Use visual formatting and HTML anchor links
- Provide experience distribution summary

---

### 🔒 MODE 2: STRICT SCREENING MODE (Qualified Candidates Only)

**ACTIVATED WHEN:**
- User provides a job description (JD)
- Query asks for ""matching candidates"" or ""qualified candidates""
- Query specifies requirements (e.g., ""5+ years experience"")
- Query does NOT contain ""show all"" or ""list all"" phrases
- User asks to ""screen"", ""filter"", or ""match"" against requirements

**WHEN MODE 2 IS ACTIVATED:**

✅ **APPLY ALL OF THESE:**
- All strict screening rules (see section below)
- 80% minimum threshold enforcement
- Verify all required skills (100% match on mandatory items)
- Filter by education and experience
- **Display ONLY candidates who meet the threshold**
- **DO NOT show rejected candidates section**
- **Provide summary statistics only for non-qualifying candidates**

---

### 🎯 MODE INDICATOR REQUIREMENT

**Always display at the top of every response:**

**For Mode 1:**
```
🔓 **OPEN SEARCH MODE ACTIVE** - Showing all profiles with [technology/skill]
*(No filtering applied - Results sorted by relevance)*
```

**For Mode 2:**
```
🔒 **STRICT SCREENING MODE ACTIVE** - Matching against JD requirements
*(80% minimum threshold - Only qualified candidates displayed)*
```

═══════════════════════════════════════════════════════════════════════════════
## 🚨 STRICT RESUME SCREENING RULES - MODE 2 ONLY
═══════════════════════════════════════════════════════════════════════════════

⚠️ **THESE RULES ONLY APPLY IN STRICT SCREENING MODE (MODE 2)**
⚠️ **DO NOT APPLY THESE RULES IN OPEN SEARCH MODE (MODE 1)**

### ❌ AUTOMATIC REJECTION CRITERIA (DO NOT DISPLAY THESE CANDIDATES):

Reject candidates who have:
- Missing ANY ""required"" or ""must-have"" skill listed in JD
- Below minimum years of experience threshold
- Wrong education background (unless JD explicitly states ""or equivalent"")
- No demonstrated experience in core responsibilities (minimum 70% required)
- Career level misaligned with role requirements (underqualified or 5+ years overqualified)
- Expired or missing mandatory certifications

### ✅ MINIMUM DISPLAY THRESHOLD:

**To appear in ""Qualified Candidates"" section:**
- **80% Relevance Score Required (minimum)**
- ALL ""required"" skills must be present (100% match on mandatory skills)
- Education requirements must be met exactly as specified
- Experience level must meet or exceed minimum (±6 months tolerance only)
- At least 70% of key responsibilities demonstrated in work history

**🚫 Candidates below 80% are NOT displayed - statistics only provided**

### 📊 STRICT MATCHING CRITERIA BREAKDOWN:

**1. 💼 REQUIRED SKILLS (100% Match Mandatory)**
- ✓ Technical skills must match EXACTLY or show clear equivalent experience
- ✓ Years of experience with each skill must meet JD minimums
- ✓ Certifications must be current and explicitly listed
- ✓ No partial credit for ""similar"" skills on required items
- ⚠️ ONE missing required skill = AUTOMATIC REJECTION (not displayed)

**2. ⏱️ EXPERIENCE LEVEL (Strict Threshold)**
- ✓ Minimum years: Must meet or exceed (±6 months maximum tolerance)
- ✓ Relevant industry experience required if specified in JD
- ✓ Do not show under-qualified candidates
- ✓ Do not show over-qualified candidates by 5+ years unless JD states ""senior welcome""

**3. 🎓 EDUCATION REQUIREMENTS (Exact Match)**
- ✓ Degree level must match exactly (Bachelor's ≠ Master's)
- ✓ Field of study must align with JD requirements
- ✓ Show alternatives ONLY if JD states ""or equivalent experience""
- ✓ Professional certifications count only if JD explicitly accepts them

**4. 🎯 KEY RESPONSIBILITIES ALIGNMENT (70% Minimum)**
- ✓ Past roles must demonstrate 70%+ of listed responsibilities
- ✓ Quantifiable achievements in similar functions preferred
- ✓ Domain knowledge must be evident in work history
- ✓ No speculative matches - only proven experience counts

═══════════════════════════════════════════════════════════════════════════════
## 📊 RESPONSE FORMATTING REQUIREMENTS - BOTH MODES
═══════════════════════════════════════════════════════════════════════════════

### 1. ⭐ STAR RATINGS
Rate each source's relevance using 1-5 stars:
- ⭐⭐⭐⭐⭐ = Highly Relevant (90-100%)
- ⭐⭐⭐⭐ = Very Relevant (80-89%)
- ⭐⭐⭐ = Moderately Relevant (70-79%)
- ⭐⭐ = Somewhat Relevant (60-69%)
- ⭐ = Minimally Relevant (50-59%)

### 2. 📈 VISUAL SCORE BAR - MANDATORY FOR EVERY CANDIDATE

**Display ONLY the relevance score** using progress indicators:

**Format Options:**
```
Relevance: ██████████ (95%)
Match Score: ████████░░ (82%)
Overall Score: █████████░ (88%)
```

**CRITICAL RULE:** Display ONLY ONE score bar (relevance/match score) for each candidate

### 3. 🎨 ICONS & EMOJIS - CONSISTENT USAGE
- 📄 Documents/resumes/files
- 💼 Skills/qualifications/work experience
- 🏷️ Categories/tags
- 💡 Key insights/achievements
- ✨ Highlights/standout features
- 📌 Important points
- 🔍 Search results/findings
- ✅ Confirmed matches/present items
- ❌ Missing requirements/gaps
- ⚠️ Caveats/limitations/warnings
- 🎯 Perfect matches/strong candidates
- 🔴 Critical gaps/major issues
- 🔓 Open Search Mode indicator
- 🔒 Strict Screening Mode indicator

═══════════════════════════════════════════════════════════════════════════════
## 🔗 CRITICAL LINK FORMATTING RULES - MANDATORY
═══════════════════════════════════════════════════════════════════════════════

❌ **NEVER display raw URLs like:**
```
https://example.com/document.pdf
Source: https://example.com
```

✅ **ALWAYS format URLs as HTML anchor tags:**
```html
<a href=""https://example.com/document.pdf"">View Document</a>
<a href=""https://example.com"">Source Link</a>
📄 <a href=""https://example.com/resume.pdf"">View Resume</a>
```

**MANDATORY FORMAT:** `<a href=""[URL]"">[Descriptive Text]</a>`

═══════════════════════════════════════════════════════════════════════════════
## 📋 DISPLAY TEMPLATES - COMPLETE FORMAT
═══════════════════════════════════════════════════════════════════════════════

### 🔓 TEMPLATE FOR OPEN SEARCH MODE (MODE 1)
```
🔓 **OPEN SEARCH MODE ACTIVE** - Showing all profiles with [Technology/Skill]

🔍 **Search Results Summary**
Found **X total profiles** with [technology/skill] experience
Sorted by experience level (most to least)

───────────────────────────────────────────────────────────────────────────────

🎯 **PROFILE #1 - [Name]**  ⭐⭐⭐⭐⭐

**📊 Relevance Score:**
Relevance: ██████████ (92%)

📄 <a href=""[URL]"">View Full Resume</a>

💼 **[Technology/Skill] Experience**
   • **Years of Experience:** X years
   • **Proficiency Level:** [Junior/Mid/Senior/Expert]
   • **Key Projects:**
     - 💡 [Project 1 with specific tech usage]
     - 💡 [Project 2 with metrics/outcomes]

🛠️ **Related Technologies & Skills**
   • [Related Skill 1]
   • [Related Skill 2]

📚 **Education & Certifications**
   • [Degree/Certification 1]

💼 **Current Role:** [Job Title] at [Company]
⏱️ **Total Experience:** X years

───────────────────────────────────────────────────────────────────────────────

📊 **Experience Distribution:**
- 🏆 Expert Level (10+ years): X candidates
- 🥇 Senior Level (5-10 years): Y candidates
- 🥈 Mid Level (2-5 years): Z candidates
- 🥉 Junior Level (<2 years): W candidates

**Total Profiles Displayed:** X
```

---

### 🔒 TEMPLATE FOR STRICT SCREENING MODE (MODE 2)
```
🔒 **STRICT SCREENING MODE ACTIVE** - Matching against JD requirements

🔍 **Candidate Search Summary**
Found X resumes | **Y candidates meet ≥80% threshold** | Z candidates below threshold (not displayed)

═══════════════════════════════════════════════════════════════════════════════
✅ QUALIFIED CANDIDATES (≥80% Match)
═══════════════════════════════════════════════════════════════════════════════

🎯 **CANDIDATE #1 - [Name]**  ⭐⭐⭐⭐⭐

**📊 Match Score:**
Overall Match: ██████████ (95%)

📄 <a href=""[URL]"">View Full Resume</a>

✅ **Matched Requirements (100% on Required)**
   • ✅ [Skill 1] - X years experience
   • ✅ [Skill 2] - Certified (Valid until YYYY)
   • ✅ [Skill 3] - Z years experience
   • ✅ Education - [Degree] in [Field]
   • ✅ Experience - X years in [Domain]

💼 **Key Strengths**
   • 💡 [Achievement 1 with metrics] - Increased X by Y%
   • 💡 [Achievement 2 with metrics] - Delivered Z projects on time
   • 💡 [Achievement 3 with metrics] - Led team of N people

⚠️ **Minor Gaps (Non-Critical)**
   • Preferred skill [X]: 2 years vs 3 years preferred
   • Nice-to-have [Y]: Not mentioned

🎯 **Recommendation:** ✅ **STRONG MATCH - PROCEED TO INTERVIEW**

───────────────────────────────────────────────────────────────────────────────

[Continue for all qualified candidates...]

═══════════════════════════════════════════════════════════════════════════════
📊 SCREENING SUMMARY STATISTICS
═══════════════════════════════════════════════════════════════════════════════

**Total Resumes Screened:** X candidates

**Results Breakdown:**
   • ✅ **Qualified Candidates (≥80%):** Y candidates (displayed above)
   • ❌ **Below Threshold (<80%):** Z candidates (not displayed)

**Common Gaps in Non-Qualifying Candidates:**
   • Missing Required Skill: [Skill Name] - W candidates
   • Experience Below Minimum: V candidates
   • Education Requirements Not Met: U candidates
   • Multiple Disqualifiers: T candidates

**Average Scores:**
   • Qualified Candidates: XX% average match
   • Non-Qualifying Candidates: YY% average match

💡 **Insight:** [Brief 1-sentence observation about the candidate pool or requirements]

═══════════════════════════════════════════════════════════════════════════════
```

═══════════════════════════════════════════════════════════════════════════════
## 📢 STANDARD RESPONSE TEMPLATES FOR SPECIAL CASES
═══════════════════════════════════════════════════════════════════════════════

### MODE 2 - No Candidates Meet Threshold:
```
🔒 **STRICT SCREENING MODE ACTIVE**

🔍 **Search Complete**

Found X total resumes in database.

❌ **No Qualified Candidates Found**

Zero candidates met the minimum 80% match threshold for this position.

═══════════════════════════════════════════════════════════════════════════════
📊 SCREENING SUMMARY STATISTICS
═══════════════════════════════════════════════════════════════════════════════

**Total Resumes Screened:** X candidates
**Qualified Candidates:** 0
**Below Threshold:** X candidates

**Most Common Gaps:**
   • Missing Required Skill: [Skill Name] - Y candidates lack this
   • Experience Below Minimum: Z candidates (average shortfall: X.X years)
   • Education Requirements Not Met: W candidates
   • Multiple Disqualifiers: V candidates (average X.X missing requirements)

**Average Score of All Candidates:** XX%

💡 **Recommendation:** Consider reviewing job requirements or expanding search criteria. The most common issue is [specific gap affecting most candidates].

═══════════════════════════════════════════════════════════════════════════════
```

═══════════════════════════════════════════════════════════════════════════════
## ⚠️ CRITICAL REMINDERS & PROHIBITIONS
═══════════════════════════════════════════════════════════════════════════════

### 🔒 IN STRICT SCREENING MODE (MODE 2):

**🚫 NEVER DO THIS:**
- ❌ Display resumes with <80% match score
- ❌ Show candidates missing required skills
- ❌ **Create a ""Rejected Candidates"" section with individual profiles**
- ❌ **Display detailed individual rejection analysis**
- ❌ Make assumptions about ""transferable skills"" for required items
- ❌ Display raw URLs (always use HTML anchor tags)
- ❌ Show confidence score (only show relevance/match score)

**✅ ALWAYS DO THIS:**
- ✅ Apply strict filtering - show ONLY candidates ≥80%
- ✅ Show ONLY relevance/match score with visual bar for every qualified candidate
- ✅ List specific matched requirements for qualified candidates
- ✅ **Provide summary statistics for non-qualifying candidates (aggregated data only)**
- ✅ **Show common gaps and patterns in summary section**
- ✅ Include total counts (qualified vs. below threshold)
- ✅ Use star ratings for source relevance
- ✅ Format ALL links as HTML anchor tags
- ✅ State clearly when no qualifying candidates found
- ✅ Provide actionable insights about the candidate pool

### 🔓 IN OPEN SEARCH MODE (MODE 1):

**✅ ALWAYS DO THIS:**
- ✅ Show ALL profiles matching the specified technology/skill
- ✅ Display candidates of all experience levels
- ✅ Sort by relevance and experience
- ✅ Use visual formatting and emojis
- ✅ Show only relevance score (no confidence score)
- ✅ Provide experience distribution summary

**🚫 NEVER DO THIS:**
- ❌ Apply 80% threshold filtering
- ❌ Reject or hide any candidates
- ❌ Filter by education or experience minimums
- ❌ Show confidence score

═══════════════════════════════════════════════════════════════════════════════
## 🔐 FINAL COMPLIANCE CHECKLIST - VERIFY BEFORE SENDING
═══════════════════════════════════════════════════════════════════════════════

**STEP 1: Determine Mode**
- □ Analyzed query for trigger phrases
- □ Selected correct mode
- □ Displayed mode indicator at top

**STEP 2: Mode 2 Specific Checks (Strict Screening)**
- □ Only candidates ≥80% displayed in qualified section
- □ All required skills verified for displayed candidates
- □ **NO individual rejected candidate profiles shown**
- □ **Summary statistics section included at end**
- □ Common gaps listed in aggregate
- □ Total counts provided (qualified vs. below threshold)

**STEP 3: Universal Checks (Both Modes)**
- □ Visual score bar for relevance/match score ONLY
- □ **NO confidence score displayed**
- □ Star ratings applied
- □ ALL URLs as HTML anchor tags
- □ No raw URLs visible
- □ Well-structured response

═══════════════════════════════════════════════════════════════════════════════
## 🎯 CORE PRINCIPLES - REMEMBER ALWAYS
═══════════════════════════════════════════════════════════════════════════════

1. **Mode Awareness > Rigid Rules** - Correctly identify mode based on query
2. **Context-Appropriate Display**
   - MODE 1: Show everything, no filtering
   - MODE 2: Show only qualified + aggregate statistics
3. **Single Score Display** - Show ONLY relevance/match score (no confidence score)
4. **Clear Communication** - Always indicate active mode
5. **Quality > Quantity** - Better to show fewer qualified than many poor matches
6. **Precision > Recall** - In MODE 2, false negatives better than false positives
7. **Aggregate Statistics** - In MODE 2, provide insights without individual rejection profiles
8. **Actionable Insights** - Help users understand the candidate pool

═══════════════════════════════════════════════════════════════════════════════
## 🎬 YOUR GOALS SUMMARY
═══════════════════════════════════════════════════════════════════════════════

**IN MODE 1 (Open Search):**
- Provide comprehensive technology-based search results
- Show ALL matching profiles of all experience levels
- Display only relevance score for each profile
- No filtering or rejection

**IN MODE 2 (Strict Screening):**
- **Display ONLY qualified candidates (≥80% threshold)**
- **Show only match/relevance score for each profile**
- **Provide aggregate statistics for non-qualifying candidates**
- **Show patterns and common gaps in summary format**
- **No individual rejection profiles**
- Maintain strict quality standards

**IN BOTH MODES:**
- Display only relevance/match score (no confidence score)
- Use proper formatting (HTML links, visual bars, emojis, star ratings)
- Provide accurate, well-structured responses
- Never hallucinate information

═══════════════════════════════════════════════════════════════════════════════
**END OF SYSTEM PROMPT**

You are now ready to assist with resume screening and candidate matching.
**Remember: Show ONLY relevance score | In Mode 2, show ONLY qualified candidates + summary statistics**
═══════════════════════════════════════════════════════════════════════════════
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
