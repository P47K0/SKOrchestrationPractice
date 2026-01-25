using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace SKOrchestrationPractice
{
    public class Embedder
    {
        private readonly OllamaApiClient _ollama;

        public Embedder(string ollamaBaseUri = "http://localhost:11434")
        {
            _ollama = new OllamaApiClient(new Uri(ollamaBaseUri));
        }

        public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be empty.", nameof(text));

            var request = new EmbedRequest()
            {
                Model = "nomic-embed-text",
                Input = [text]
            };

            var response = await _ollama.EmbedAsync(request);

            if (response.Embeddings == null || !response.Embeddings.Any())
                throw new InvalidOperationException("No embeddings returned from Ollama.");

            // Get the first (and only) embedding vector, convert double[] to float[]
            var embeddingDoubles = response.Embeddings[0];
            var embeddingFloats = embeddingDoubles.Select(d => (float)d).ToArray();

            if (embeddingFloats.Length != 768)
                throw new InvalidOperationException($"Unexpected embedding dimension: {embeddingFloats.Length} (expected 1024)");

            return new ReadOnlyMemory<float>(embeddingFloats);
        }
    }
}
