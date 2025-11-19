using Azure.AI.OpenAI;
using Azure;
using OpenAI.Chat;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace AIResumeScanner_Razden.Models
{
    public class ProfileValidationService
    {

        //TODO: Implementing alternative way to check thru schema validation
        //string[] requiredFields = { "full_name", "email", "phone", "location", "professional_summary", "skills", "total_experience_years" };

        private IConfiguration _configuration;
        public ProfileValidationService()
        {
            var builder = new ConfigurationBuilder()
                  .SetBasePath(Directory.GetCurrentDirectory())
                  .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                  .AddEnvironmentVariables();
            _configuration = builder.Build();
        }
        public async Task<ValidationResult> ValidateResumeDocument(string documentText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(documentText) || documentText.Length < 100)
                {
                    return new ValidationResult
                    {
                        match = false,
                        summary = "Document is too short or empty to be a valid resume.",
                        confidence = 0
                    };
                }

                var resumePrompt = _configuration["ResumeMetaDataPrompt"];
                if (string.IsNullOrEmpty(resumePrompt))
                {
                    Console.WriteLine("Please set the prompt for resume in app.settings.json");
                }

                var azureOpenAITokenCount = _configuration.GetSection("AzureOpenAI")["ChatTokenCount"];
                if (string.IsNullOrEmpty(azureOpenAITokenCount))
                {
                    Console.WriteLine("Please set the token count in app.settings.json");
                }
                var messages = new List<OpenAI.Chat.ChatMessage>
                                                        {
                                                           new SystemChatMessage(resumePrompt),
                                                           new UserChatMessage(documentText)
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

                //

                var validationPrompt = $@"
                                            You are a JSON validation expert. Your task is to determine if the extracted JSON matches the requirements specified in the original prompt.

                                            Original Prompt:
                                            {resumePrompt}

                                            Extracted JSON:
                                            {cleanJson}

                                            Required Fields to Validate:
- full_name (string or null)
- email (string or null)
- phone (string or null)
- location (string or null)
- professional_summary (string or null)
- skills (array)
- total_experience_years (number or null)
- work_experience (array of objects with: job_title, company_name, start_date, end_date, location, description)
- projects (array of objects with: title, description, technologies)

Analyze whether the extracted JSON:
1. Contains **all required fields** listed above
2. Uses the **correct data types** for all fields
3. Matches the **required structure**, including nested objects
4. Includes **all required subfields** for items in arrays


                                            Respond with ONLY a JSON object in this exact format:
                                            {{
                                              ""match"": true/false,
                                              ""confidence"": 0.0-1.0,
                                              ""missing_fields"": [""field1"", ""field2""],
                                              ""type_mismatches"": [""field3 should be string but is number""],
                                              ""structural_issues"": [""description of any structural problems""],
                                              ""summary"": ""brief explanation""
                                            }}";


                var resumeValidation = new List<OpenAI.Chat.ChatMessage>
                                                        {
                                                           new SystemChatMessage("You are a precise JSON validation assistant. Always respond with valid JSON only."),
                                                           new UserChatMessage(validationPrompt)
                                                        };

                ChatCompletion resumeValidationCompletion = await chatClient.CompleteChatAsync(resumeValidation, options);

                var validationResult = new ValidationResult();
                string cleanResumeValidationJson = string.Empty;
                if (resumeValidationCompletion != null)
                {
                    // Get the assistant's response content (the JSON string)
                    string responseJson = resumeValidationCompletion.Content[0].Text.ToString();

                    var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                    if (match.Success)
                    {
                        cleanResumeValidationJson = match.Value;
                        cleanResumeValidationJson = cleanResumeValidationJson.Trim().Replace("```json", "").Replace("```", "").Trim();

                        validationResult = JsonSerializer.Deserialize<ValidationResult>(cleanResumeValidationJson);
                        
                    }
                }
                return validationResult;

            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                };
            }

        }

    }

    public class ValidationResult
    {
        public bool match { get; set; }
        public double confidence { get; set; }
        public List<object> missing_fields { get; set; }
        public List<object> type_mismatches { get; set; }
        public List<object> structural_issues { get; set; }
        public string summary { get; set; }
    }
}
