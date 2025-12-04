using Azure.AI.OpenAI;
using Azure;
using OpenAI.Chat;
using System.Text.RegularExpressions;

namespace AIResumeScanner_Razden.Services
{
    public  class ExtractMetaDataForResume
    {
        private  IConfiguration _configuration;
      

        public  async Task<string> ExtractMetaData(string content)
        {

            try
            {
                var builder = new ConfigurationBuilder()
                   .SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                   .AddEnvironmentVariables();
                _configuration = builder.Build();

                var resumePrompt = _configuration["ResumeMetaDataPrompt"];
                if (string.IsNullOrEmpty(resumePrompt))
                {
                    Console.WriteLine("Please set the prompt for resume in app.settings.json");                   
                }

                var gptModel = _configuration.GetSection("AzureOpenAI")["ChatDeploymentName"];
                if (string.IsNullOrEmpty(gptModel))
                {
                    Console.WriteLine("Please set the GPT model in app.settings.json");
                }


                if(gptModel?.ToLower()== "gpt-4o")
                {
                    var azureOpenAITokenCount = _configuration.GetSection("AzureOpenAI")["ChatTokenCount"];
                    if (string.IsNullOrEmpty(azureOpenAITokenCount))
                    {
                        Console.WriteLine("Please set the token count in app.settings.json");
                    }


                    var messages = new List<ChatMessage>
                                                        {
                                                           new SystemChatMessage(resumePrompt),
                                                           new UserChatMessage(content)
                                                        };

                    // Create chat completion options
                    var options = new ChatCompletionOptions
                    {
                        Temperature = (float)0.7,
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


                    if (completion != null)
                    {
                        // Get the assistant's response content (the JSON string)
                        string responseJson = completion.Content[0].Text.ToString();

                        var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                        if (match.Success)
                        {

                            string cleanJson = match.Value;
                            return cleanJson;
                        }
                    }
                }
                else if (gptModel?.ToLower() == "gpt-5-nano")
                {
                    var messages = new List<ChatMessage>
                                                        {
                                                           new SystemChatMessage(resumePrompt),
                                                           new UserChatMessage(content)
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


                    if (completion != null)
                    {
                        // Get the assistant's response content (the JSON string)
                        string responseJson = completion.Content[0].Text.ToString();

                        var match = Regex.Match(responseJson, @"(\{[\s\S]*\}|\[[\s\S]*\])");
                        if (match.Success)
                        {

                            string cleanJson = match.Value;
                            return cleanJson;
                        }
                    }

                }



            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting metadata: {ex.Message}");
                return ex.Message;
            }

            return "";
        }
    }
}
