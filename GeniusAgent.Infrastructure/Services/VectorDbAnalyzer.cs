using GeniusAgent.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Embeddings;
using Qdrant.Client;
using System.ClientModel;
using System.Text;

namespace GeniusAgent.Infrastructure.Services;

public class VectorDbAnalyzer : IRepositoryAnalyzer
{
    private readonly QdrantClient _qdrantClient;
    private readonly EmbeddingClient _embeddingClient;
    private readonly string _collectionName;

    public VectorDbAnalyzer(IConfiguration config)
    {
        _collectionName = config["VectorDb:CollectionName"] ?? "genius-agent-local-nomic";

        // Local Ollama model (nomic-embed-text is highly recommended for RAG)
        var embeddingModel = config["OpenAi:EmbeddingModel"] ?? "nomic-embed-text";

        // 1. Configure the OpenAI SDK to talk to local Ollama
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(config["OpenAi:Endpoint"] ?? "http://localhost:11434/v1")
        };

        // Key is required by SDK but ignored by Ollama
        var client = new OpenAIClient(new ApiKeyCredential("ollama"), options);
        _embeddingClient = client.GetEmbeddingClient(embeddingModel);

        // 2. Configure Qdrant Client
        _qdrantClient = new QdrantClient(new Uri(config["VectorDb:Endpoint"] ?? "http://localhost:6334"));
    }

    public async Task<string> GetRelevantPatternsAsync(string userIntent)
    {
        if (string.IsNullOrWhiteSpace(userIntent))
        {
            return string.Empty;
        }

        float[] queryVector;

        try
        {
            var response = await _embeddingClient.GenerateEmbeddingAsync(userIntent);
            queryVector = response.Value.ToFloats().ToArray();

            if (queryVector.Length == 0)
            {
                return string.Empty;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Embedding generation failed: {ex.Message}");
            return string.Empty;
        }

        // Search Qdrant for matches
        var searchResults = await _qdrantClient.SearchAsync(_collectionName, queryVector, limit: 5);

        if (searchResults is null || searchResults.Count == 0)
        {
            return string.Empty;
        }

        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("Existing repository patterns for consistency:");

        foreach (var hit in searchResults)
        {
            if (hit.Payload is null
                || !hit.Payload.TryGetValue("file_path", out var filePathValue)
                || !hit.Payload.TryGetValue("content", out var contentValue))
            {
                continue;
            }

            var path = filePathValue.StringValue;
            var code = contentValue.StringValue;
            contextBuilder.AppendLine($"--- File: {path} ---\n{code}\n");
        }

        return contextBuilder.ToString();
    }
}