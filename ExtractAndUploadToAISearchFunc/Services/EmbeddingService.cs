using Azure.AI.OpenAI;
using Azure;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExtractAndUploadToAISearchFunc.Services
{
    public class EmbeddingService
    {
        private readonly OpenAIClient _openAIClient;
        private readonly string _embeddingModel;

        public EmbeddingService()
        {

        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            string endpoint = "https://resumeazureopenai.openai.azure.com/";
            string apiKey = "ATa4k79e6ds8IHArihSVeFcOqwIh1aVyabfwFhbUsf5bpGj9IEXwJQQJ99BJACYeBjFXJ3w3AAABACOGpMq1";
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



        [NonAction]
        public async Task<float[]> GenerateEmbedding(string text)
        {
            string endpoint = "https://resumeazureopenai.openai.azure.com/";
            string apiKey = "ATa4k79e6ds8IHArihSVeFcOqwIh1aVyabfwFhbUsf5bpGj9IEXwJQQJ99BJACYeBjFXJ3w3AAABACOGpMq1";
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


        public async Task<List<float[]>> GenerateEmbeddingsForChunksAsync(List<string> chunks)
        {
            string endpoint = "https://resumeazureopenai.openai.azure.com/";
            string apiKey = "ATa4k79e6ds8IHArihSVeFcOqwIh1aVyabfwFhbUsf5bpGj9IEXwJQQJ99BJACYeBjFXJ3w3AAABACOGpMq1";
            string deploymentName = "text-embedding-ada-002"; // or your custom deployment name

            var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            var embeddingClient = client.GetEmbeddingClient(deploymentName);

            var embeddings = new List<float[]>();

            // Process in batches to avoid rate limits
            const int batchSize = 16;

            for (int i = 0; i < chunks.Count; i += batchSize)
            {
                try
                {
                    var batch = chunks.Skip(i).Take(batchSize).ToList();

                    // Generate embeddings for the batch
                    var response = await embeddingClient.GenerateEmbeddingsAsync(batch);

                    // Extract embeddings from response
                    foreach (var embedding in response.Value)
                    {
                        embeddings.Add(embedding.ToFloats().ToArray());
                    }

                    Console.WriteLine($"Processed {Math.Min(i + batchSize, chunks.Count)}/{chunks.Count} chunks");

                    // Add delay to respect rate limits
                    if (i + batchSize < chunks.Count)
                        await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing batch at index {i}: {ex.Message}");
                    throw;
                }
            }

            return embeddings;
        }


        // Alternative: Process with progress callback
        public async Task<List<float[]>> GenerateEmbeddingsWithProgressAsync(
            List<string> chunks,
            IProgress<int> progress = null)
        {
            string endpoint = "https://resumeazureopenai.openai.azure.com/";
            string apiKey = "ATa4k79e6ds8IHArihSVeFcOqwIh1aVyabfwFhbUsf5bpGj9IEXwJQQJ99BJACYeBjFXJ3w3AAABACOGpMq1";
            string deploymentName = "text-embedding-ada-002"; // or your custom deployment name

            var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            var embeddingClient = client.GetEmbeddingClient(deploymentName);

            var embeddings = new List<float[]>();
            const int batchSize = 16;

            for (int i = 0; i < chunks.Count; i += batchSize)
            {
                var batch = chunks.Skip(i).Take(batchSize).ToList();
                var response = await embeddingClient.GenerateEmbeddingsAsync(batch);

                foreach (var embedding in response.Value)
                {
                    embeddings.Add(embedding.ToFloats().ToArray());
                }

                progress?.Report(Math.Min(i + batchSize, chunks.Count));

                if (i + batchSize < chunks.Count)
                    await Task.Delay(100);
            }

            return embeddings;
        }

        public async Task<float[]> AverageVectors(List<float[]> vectors)
        {
            if (vectors == null || vectors.Count == 0)
                return Array.Empty<float>();

            int dimensions = vectors[0].Length;
            var averaged = new float[dimensions];

            foreach (var vector in vectors)
            {
                for (int i = 0; i < dimensions; i++)
                {
                    averaged[i] += vector[i];
                }
            }

            for (int i = 0; i < dimensions; i++)
            {
                averaged[i] /= vectors.Count;
            }

            return averaged;
        }

    }
}
