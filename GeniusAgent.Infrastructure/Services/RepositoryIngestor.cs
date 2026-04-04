using Microsoft.Extensions.Configuration;
using OpenAI.Embeddings;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using System.Text;

namespace GeniusAgent.Infrastructure.Services;

public class RepositoryIngestor(IConfiguration config, EmbeddingClient embeddingClient, QdrantClient qdrantClient)
{
    private readonly string _collectionName = config["VectorDb:CollectionName"] ?? "genius-agent-local-nomic";

    public async Task IndexKnowledgeBaseAsync(string rootPath)
    {
        // Define supported enterprise artifact types
        var supportedExtensions = new[] { ".cs", ".md", ".pdf", ".txt", ".json" };

        var files = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()) &&
                        !f.Contains("bin") && !f.Contains("obj"));

        foreach (var filePath in files)
        {
            var content = await File.ReadAllTextAsync(filePath);
            var embedding = await GetEmbeddingAsync(content);

            var point = new PointStruct
            {
                Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                Vectors = embedding.ToArray(),
                Payload =
                {
                    ["file_path"] = Path.GetRelativePath(rootPath, filePath),
                    ["content"] = content,
                    // New: Catalog Metadata
                    ["artifact_domain"] = filePath.Contains("docs") ? "Design" : "Implementation",
                    ["is_authoritative"] = !filePath.Contains("temp")
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
}