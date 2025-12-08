using Azure.AI.OpenAI;
using Azure;
using OpenAI.Chat;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;

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
                        IsValid = false,
                        Message = "Document is too short or empty to be a valid resume.",
                        Confidence = 0
                    };
                }
                var validationResult = new ValidationResult();
                var gptModel = _configuration.GetSection("AzureOpenAI")["ChatDeploymentName"];
                if (string.IsNullOrEmpty(gptModel))
                {
                    Console.WriteLine("Please set the GPT model in app.settings.json");
                }

                if (gptModel?.ToLower() == "gpt-4o")
                {
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
- total_experience_years (number or string)
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
                    

                }

                if(gptModel?.ToLower() == "gpt-5-nano")
                {
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
                        Temperature = (float)1,
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
- full_name (string)
- email (string)
- phone (string)
- professional_summary (string)
- skills (array)
- total_experience_years (string or number)
- work_experience (array of objects with: job_title, company_name, start_date, end_date, location, description)
- projects (array of objects with: title, description, technologies)


Validation Rules:
1. Check if **all required fields** listed above are present in the JSON
2. Verify each field uses the **correct data type**
3. Ensure the **structure matches**, including nested objects and arrays
4. Validate that array items contain **all required subfields**
5. **IMPORTANT**: If any required field is missing, list it explicitly and explain its importance in the summary

Analysis Steps:
- Compare the extracted JSON against each required field
- Identify which fields are present and which are missing
- Check data type correctness for all present fields
- Validate nested structure for arrays (work_experience, projects)
- If validation fails, clearly state which specific fields are missing

Respond with ONLY a JSON object in this exact format:
{{
  ""IsValid"": true/false,
  ""Confidence"": 0.0-100.0,
  ""Message"": ""brief validation message"",
  ""DocumentType"": ""Resume/CV/Profile/Other"",
  ""MatchedSections"": [""list of sections found like: full_name, professional_summary, total_experience_years, work_experience, skills, projects""],
  ""Reason"": ""detailed explanation that MUST include: 
    - If IsValid is false: List ALL missing required fields explicitly (e.g., 'Missing required fields: full_name, professional_summary, total_experience_years, work_experience, skills, projects')
    - Any type mismatches found
    - Any structural issues
    - Why the document failed or passed validation
    - For missing fields, explain why they are essential for a valid resume""
}}

Example Reason for invalid document:
""This document is missing critical required fields: full_name, professional_summary, total_experience_years, work_experience, skills, projects. A valid resume must contain contact information (name and email) to identify the candidate, and work_experience to demonstrate professional background. Additionally, the skills field is empty which is required to show candidate capabilities.""

Example Reason for valid document:
""All required fields are present with correct data types. The document contains complete contact information, professional summary, work experience with proper structure, skills array. Confidence is high at 95% as all validation criteria are met.""




";


                    var resumeValidation = new List<OpenAI.Chat.ChatMessage>
                                                        {
                                                           new SystemChatMessage("You are a precise JSON validation assistant. Always respond with valid JSON only."),
                                                           new UserChatMessage(validationPrompt)
                                                        };

                    ChatCompletion resumeValidationCompletion = await chatClient.CompleteChatAsync(resumeValidation, options);


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
                            validationResult.DocumentType = "Resume";

                        }
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

        public static ValidationResponse ValidateResumeJson(string jsonString)
        {
            var response = new ValidationResponse { IsValid = true };

            try
            {
                var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                // Check top-level required fields
                CheckStringField(root, "full_name", response);
                CheckStringField(root, "email", response);
                CheckStringField(root, "phone", response);
                CheckStringField(root, "location", response);
                CheckStringField(root, "professional_summary", response);
                CheckNumberField(root, "total_experience_years", response);

                // Check skills array
                CheckArrayField(root, "skills", response);

                // Check work_experience array and its nested fields
                CheckWorkExperience(root, response);

                // Check projects array and its nested fields
                CheckProjects(root, response);

                // Set validation result
                if (response.MissingFields.Count > 0 || response.EmptyFields.Count > 0)
                {
                    response.IsValid = false;
                    response.Message = BuildErrorMessage(response);
                }
                else
                {
                    response.IsValid = true;
                    response.Message = "All required fields are present and have valid values.";
                }
            }
            catch (JsonException ex)
            {
                response.IsValid = false;
                response.Message = $"Invalid JSON format: {ex.Message}";
            }
            catch (Exception ex)
            {
                response.IsValid = false;
                response.Message = $"Validation error: {ex.Message}";
            }

            return response;
        }

        private static void CheckStringField(JsonElement root, string fieldName, ValidationResponse response)
        {
            if (!root.TryGetProperty(fieldName, out JsonElement field))
            {
                response.MissingFields.Add(fieldName);
            }
            else if (field.ValueKind == JsonValueKind.Null)
            {
                // Null is acceptable for nullable fields
                return;
            }
            else if (field.ValueKind == JsonValueKind.String)
            {
                var value = field.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    response.EmptyFields.Add(fieldName);
                }
            }
            else
            {
                response.EmptyFields.Add($"{fieldName} (invalid type)");
            }
        }

        private static void CheckNumberField(JsonElement root, string fieldName, ValidationResponse response)
        {
            if (!root.TryGetProperty(fieldName, out JsonElement field))
            {
                response.MissingFields.Add(fieldName);
            }
            else if (field.ValueKind == JsonValueKind.Null)
            {
                // Null is acceptable for nullable fields
                return;
            }
            else if (field.ValueKind != JsonValueKind.Number)
            {
                response.EmptyFields.Add($"{fieldName} (should be a number)");
            }
        }

        private static void CheckArrayField(JsonElement root, string fieldName, ValidationResponse response)
        {
            if (!root.TryGetProperty(fieldName, out JsonElement field))
            {
                response.MissingFields.Add(fieldName);
            }
            else if (field.ValueKind == JsonValueKind.Null)
            {
                response.EmptyFields.Add($"{fieldName} (null array)");
            }
            else if (field.ValueKind != JsonValueKind.Array)
            {
                response.EmptyFields.Add($"{fieldName} (should be an array)");
            }
            else if (field.GetArrayLength() == 0)
            {
                response.EmptyFields.Add($"{fieldName} (empty array)");
            }
        }

        private static void CheckWorkExperience(JsonElement root, ValidationResponse response)
        {
            if (!root.TryGetProperty("work_experience", out JsonElement workExpArray))
            {
                response.MissingFields.Add("work_experience");
                return;
            }

            if (workExpArray.ValueKind == JsonValueKind.Null)
            {
                response.EmptyFields.Add("work_experience (null array)");
                return;
            }

            if (workExpArray.ValueKind != JsonValueKind.Array)
            {
                response.EmptyFields.Add("work_experience (should be an array)");
                return;
            }

            if (workExpArray.GetArrayLength() == 0)
            {
                response.EmptyFields.Add("work_experience (empty array)");
                return;
            }

            // Check each work experience item for required nested fields
            int index = 0;
            foreach (var item in workExpArray.EnumerateArray())
            {
                var prefix = $"work_experience[{index}]";

                CheckNestedStringField(item, "job_title", prefix, response);
                CheckNestedStringField(item, "company_name", prefix, response);
                CheckNestedStringField(item, "start_date", prefix, response);
                CheckNestedStringField(item, "end_date", prefix, response);
                CheckNestedStringField(item, "location", prefix, response);
                CheckNestedStringField(item, "description", prefix, response);

                index++;
            }
        }

        private static void CheckProjects(JsonElement root, ValidationResponse response)
        {
            if (!root.TryGetProperty("projects", out JsonElement projectsArray))
            {
                response.MissingFields.Add("projects");
                return;
            }

            if (projectsArray.ValueKind == JsonValueKind.Null)
            {
                response.EmptyFields.Add("projects (null array)");
                return;
            }

            if (projectsArray.ValueKind != JsonValueKind.Array)
            {
                response.EmptyFields.Add("projects (should be an array)");
                return;
            }

            if (projectsArray.GetArrayLength() == 0)
            {
                response.EmptyFields.Add("projects (empty array)");
                return;
            }

            // Check each project item for required nested fields
            int index = 0;
            foreach (var item in projectsArray.EnumerateArray())
            {
                var prefix = $"projects[{index}]";

                CheckNestedStringField(item, "title", prefix, response);
                CheckNestedStringField(item, "description", prefix, response);

                // Check technologies array
                if (!item.TryGetProperty("technologies", out JsonElement techArray))
                {
                    response.MissingFields.Add($"{prefix}.technologies");
                }
                else if (techArray.ValueKind == JsonValueKind.Null)
                {
                    response.EmptyFields.Add($"{prefix}.technologies (null array)");
                }
                else if (techArray.ValueKind != JsonValueKind.Array)
                {
                    response.EmptyFields.Add($"{prefix}.technologies (should be an array)");
                }
                else if (techArray.GetArrayLength() == 0)
                {
                    response.EmptyFields.Add($"{prefix}.technologies (empty array)");
                }

                index++;
            }
        }

        private static void CheckNestedStringField(JsonElement element, string fieldName, string prefix, ValidationResponse response)
        {
            if (!element.TryGetProperty(fieldName, out JsonElement field))
            {
                response.MissingFields.Add($"{prefix}.{fieldName}");
            }
            else if (field.ValueKind == JsonValueKind.Null)
            {
                response.EmptyFields.Add($"{prefix}.{fieldName} (null)");
            }
            else if (field.ValueKind == JsonValueKind.String)
            {
                var value = field.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    response.EmptyFields.Add($"{prefix}.{fieldName} (empty string)");
                }
            }
            else
            {
                response.EmptyFields.Add($"{prefix}.{fieldName} (invalid type)");
            }
        }
        private static string BuildErrorMessage(ValidationResponse response)
        {
            var messages = new List<string>();

            if (response.MissingFields.Count > 0)
            {
                messages.Add($"{response.MissingFields.Count}): These critical fields are not present in the resume.\n");
            }

            if (response.EmptyFields.Count > 0)
            {
                messages.Add($"Invalid/Empty ({response.EmptyFields.Count}): These fields exist but contain no meaningful data.\n");
            }

            messages.Add($"\n💡 Tip: A complete resume should include contact information, professional summary, ");
            messages.Add($"skills, work experience with detailed descriptions, and relevant projects.");


            return string.Join(". ", messages);
        }

    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public double Confidence { get; set; }

        public string Message { get; set; }

        public string DocumentType { get; set; }

        public List<string>  MatchedSections { get; set; }
       
        public string Reason { get; set; }
    }

    public class ValidationResponse
    {
        public bool IsValid { get; set; }
        public List<string> MissingFields { get; set; } = new();
        public List<string> EmptyFields { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }
}
