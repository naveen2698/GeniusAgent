using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Embeddings;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GeniusAgent.Infrastructure.Services;

public class RepositoryIngestor(IConfiguration config, EmbeddingClient embeddingClient, QdrantClient qdrantClient)
{
    private readonly string _collectionName = config["VectorDb:CollectionName"] ?? "genius-agent-local-nomic";

    public async Task IndexRepositoryAsync(string repoPath)
    {
        // 1. Ensure Collection exists in Qdrant
        var collections = await qdrantClient.ListCollectionsAsync();
        if (!collections.Contains(_collectionName))
        {
            await qdrantClient.CreateCollectionAsync(_collectionName,
                new VectorParams { Size = 768, Distance = Distance.Cosine });
        }

        // 2. Scan for .cs files (excluding bin/obj/migrations)
        var files = Directory.GetFiles(repoPath, "*.*", SearchOption.AllDirectories)
     .Where(f => f.EndsWith(".cs") || f.EndsWith(".md") || f.EndsWith(".txt") || f.EndsWith(".pdf"));

        foreach (var filePath in files)
        {
            var content = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(content)) continue;

            // 3. Generate Embedding for the file content
            var embedding = await GetEmbeddingAsync(content);

            // 4. Upsert into Qdrant with Metadata (deterministic ID so re-indexing updates existing points)
            var relativePath = Path.GetRelativePath(repoPath, filePath);
            var deterministicId = DeterministicGuid(relativePath);
            var point = new PointStruct
            {
                Id = new PointId { Uuid = deterministicId.ToString() },
                Vectors = embedding.ToArray(),
                Payload =
                {
                    ["file_path"] = relativePath,
                    ["content"] = content,
                    ["last_indexed"] = DateTime.UtcNow.ToString("O")
                }
            };

            await qdrantClient.UpsertAsync(_collectionName, new[] { point });
        }
    }

    private async Task<ReadOnlyMemory<float>> GetEmbeddingAsync(string text)
    {
        var response = await embeddingClient.GenerateEmbeddingAsync(text);
        return response.Value.ToFloats();
    }

    private static Guid DeterministicGuid(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.AsSpan(0, 16));
    }
}