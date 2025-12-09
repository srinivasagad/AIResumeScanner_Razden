using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Search;
using System.Net;
using SearchIndexClient = Azure.Search.Documents.Indexes.SearchIndexClient;


namespace ResumeParserWebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CustomAISearchIndexController : ControllerBase
    {
        private readonly ILogger<FileUploaderController> _logger;
        private readonly IConfiguration _configuration;
        public CustomAISearchIndexController(ILogger<FileUploaderController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost]
        public async Task<IActionResult> CreateCustomAISearchIndexAsync()
        {
            var azureAISearch = _configuration.GetSection("AISearch");
            var apiKey = azureAISearch["SearchApiKey"];
            var serviceEndpoint = azureAISearch["ServiceEndpoint"];
            var indexName = "";// azureAISearch["SearchIndexName"];

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(serviceEndpoint) || string.IsNullOrEmpty(indexName))
                return BadRequest("Missing configuration.");

            var credential = new AzureKeyCredential(apiKey);
            var indexClient = new SearchIndexClient(new Uri(serviceEndpoint), credential);

            ///
            //Create synonym map
            //var synonymMap = new SynonymMap(
            //                                    name: "skills-synonyms",
            //                                    synonyms: @"dotnet, .NET
            //                                js, javascript
            //                                node, nodejs
            //                                dotnet core, .NET Core, .netcore, dotnetcore, netcore, dotnet"
            //                                );

            var synonymMap = new SynonymMap(
                                               name: "skills-synonyms",
                                               synonyms: @"js, javascript
                                            node, nodejs
                                            c#, c sharp, c-sharp, csharp, C#.Net
                                            .net framework, dotnet framework, .netframework, dotnetframework, .net, dotnet, .net core, dotnet core, .netcore, dotnetcore, netcore, .net standard, dotnet standard, .netstandard, dotnetstandard
                                            blazor, razor components, asp.net blazor, blazor webassembly, blazor server\nwebassembly, wasm"

                                           );

            // await indexClient.CreateOrUpdateSynonymMapAsync(synonymMap);


            ///

            var projectFields = new List<SearchField>
            {
                new SearchableField("title") { IsFilterable = true },
                new SearchableField("description") { IsFilterable = true },
                new SimpleField("technologies", SearchFieldDataType.Collection(SearchFieldDataType.String))
                {
                    IsFilterable = true,
                    IsFacetable = true
                }
            };

            var projectsField = new SearchField("projects", SearchFieldDataType.Collection(SearchFieldDataType.Complex));
            foreach (var field in projectFields)
            {
                projectsField.Fields.Add(field);
            }

            var educationFields = new List<SearchField>
            {
                new SearchableField("degree") { IsFilterable = true },
                new SearchableField("field_of_study") { IsFilterable = true },
                new SearchableField("institution_name") { IsFilterable = true },
                new SearchableField("start_date") { IsFilterable = true },
                new SearchableField("end_date") { IsFilterable = true }
            };

            var educationField = new SearchField("education", SearchFieldDataType.Collection(SearchFieldDataType.Complex));
            foreach (var field in educationFields)
            {
                educationField.Fields.Add(field);
            }

            var workExperienceFields = new List<SearchField>
           {
               new SearchableField("job_title") { IsFilterable = true },
               new SearchableField("company_name") { IsFilterable = true },
               new SearchableField("start_date") { IsFilterable = true },
               new SearchableField("end_date") { IsFilterable = true },
               new SearchableField("location") { IsFilterable = true },
               new SearchableField("description") { IsFilterable = true }
           };

            var workExperienceField = new SearchField("work_experience", SearchFieldDataType.Collection(SearchFieldDataType.Complex));
            foreach (var field in workExperienceFields)
            {
                workExperienceField.Fields.Add(field);
            }


            var index = new SearchIndex(indexName)
            {
                Fields = new List<SearchField>
               {
                   new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                   new SearchableField("full_name") { IsFilterable = true },
                   new SearchableField("email") { IsFilterable = true },
                   new SearchableField("phone") { IsFilterable = true },
                   new SearchableField("location") { IsFilterable = true },
                   new SearchableField("professional_summary") { IsFilterable = true,  },
                   new SearchableField("skills", collection: true)
                   {
                        IsFilterable = true,
                        IsFacetable = true,
                        SynonymMapNames = { "skills-synonyms" }
                   },
                   //new SimpleField("skills", SearchFieldDataType.Collection(SearchFieldDataType.String))
                   //{
                   //    IsFilterable = true,
                   //    IsFacetable = true,
                       
                   //},
                   new SimpleField("total_experience_years", SearchFieldDataType.Double) { IsFilterable = true, IsSortable = true },

                   workExperienceField,
                   educationField,
                   new SimpleField("certifications", SearchFieldDataType.Collection(SearchFieldDataType.String))
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                    },
                   projectsField,
                   new SimpleField("languages", SearchFieldDataType.Collection(SearchFieldDataType.String))
                   {
                            IsFilterable = true,
                            IsFacetable = true,
                   },
                    new SimpleField("resume_url", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("resume_id", SearchFieldDataType.String) { IsFilterable = true },
                   // new SimpleField("upload_date", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    new SearchableField("file_name") { IsFilterable = true },
                   // new SimpleField("availability", SearchFieldDataType.Boolean) { IsFilterable = true },
                   // new SimpleField("isactive", SearchFieldDataType.Boolean) { IsFilterable = true },
                    new SearchableField("fullText") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                    new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = 1536, // for text-embedding-3-small use 1536
                        VectorSearchProfileName = "my-vector-profile"
                    }

               },
                VectorSearch = new VectorSearch
                {
                    Profiles =
                                {
                                    new VectorSearchProfile("my-vector-profile", "my-hnsw-config")
                                },
                    Algorithms =
                                {
                                    new HnswAlgorithmConfiguration("my-hnsw-config")
                                }
                }
            };

            await indexClient.CreateOrUpdateIndexAsync(index);

            return Ok($"Custom AI search index '{indexName}' created successfully.");

        }


        [HttpPost("DashboardIndexCreation")]
        public async Task<IActionResult> CreateAISearchIndexForDashboardAsync()
        {
            var azureAISearch = _configuration.GetSection("AISearch");
            var apiKey = azureAISearch["SearchApiKey"];
            var serviceEndpoint = azureAISearch["ServiceEndpoint"];
            var indexName = azureAISearch["SearchDashboardIndexName"];

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(serviceEndpoint) || string.IsNullOrEmpty(indexName))
                return BadRequest("Missing configuration.");

            var credential = new AzureKeyCredential(apiKey);
            var indexClient = new SearchIndexClient(new Uri(serviceEndpoint), credential);

            var index = new SearchIndex(indexName)
            {
                Fields = new List<SearchField>
                {
                    new SimpleField("Id", SearchFieldDataType.String)
                        {
                            IsKey = true,
                            IsFilterable = true
                        },
                        new SearchableField("Title")
                        {
                            IsFilterable = true,
                            IsSortable = true
                        },
                        new SearchableField("Name")
                        {
                            IsFilterable = true,
                            IsSortable = true
                        },
                        new SearchableField("Email")
                        {
                            IsFilterable = true,
                            IsSortable = true
                        },
                        new SearchableField("Content"),
                        new SearchableField("chunks", collection: true),
                        new SearchField("chunkVectors", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                        {
                            IsSearchable = true,
                            VectorSearchDimensions = 1536, // for text-embedding-3-small use 1536
                            VectorSearchProfileName = "my-vector-profile"
                        },
                        new SearchableField("Category")
                        {
                            IsFilterable = true,
                            IsFacetable = true
                        },
                        new SearchableField("Skills", collection: true)
                        {
                            IsFilterable = true
                        },
                        new SimpleField("ExperienceYears", SearchFieldDataType.Double)
                        {
                            IsFilterable = true,
                            IsSortable = true
                        },
                        new SearchableField("Location")
                        {
                            IsFilterable = true,
                            IsSortable = true
                        },
                        new SimpleField("UploadDate", SearchFieldDataType.DateTimeOffset)
                        {
                            IsFilterable = true,
                            IsSortable = true
                        }
                        

                },
                VectorSearch = new VectorSearch
                {
                    Profiles =
                                {
                                    new VectorSearchProfile("my-vector-profile", "my-hnsw-config")
                                },
                    Algorithms =
                                {
                                    new HnswAlgorithmConfiguration("my-hnsw-config")
                                }
                }
            };
            await indexClient.CreateOrUpdateIndexAsync(index);

            return Ok($"Custom AI search index '{indexName}' created successfully.");
        }
    }
}
