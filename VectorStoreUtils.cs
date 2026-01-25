using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace SKOrchestrationPractice
{
    internal static class VectorStoreUtils
    {
        private static string CollectionName = "words";

        public static async Task<string[]?> GetWordsAsync(
            this SqliteVectorStore source,
            string jsonFilePath,
            VectorSearchOptions<HangmanWordRecord> searchOptions,
            Embedder embedder,
            string queryText)
        {
            if (!File.Exists(jsonFilePath))
            {
                Console.WriteLine($"ERROR: File not found: {jsonFilePath}");
                return null;
            }

            // Create collection
            var collection = source.GetCollection<int, HangmanWordRecord>(CollectionName);

            var exists = await source.CollectionExistsAsync(CollectionName);

            // Load & embed JSON
            string json = await File.ReadAllTextAsync(jsonFilePath);
            var embeddingFile = JsonSerializer.Deserialize<EmbeddingFile>(json);

            if (embeddingFile?.Records == null || !embeddingFile.Records.Any())
            {
                Console.WriteLine("No entries found in JSON.");
                return null;
            }

            var embeddingFileRecords= embeddingFile.Records;

            var records = new List<HangmanWordRecord>(embeddingFileRecords.Length);

            Console.WriteLine($"Embedding {embeddingFileRecords.Length} words...");

            foreach (var entry in embeddingFileRecords)
            {
                int length = entry.Length;
                string textToEmbed = $"{entry.Word}: {entry.Language} Category: {entry.Category}";

                var vector = await embedder.GenerateEmbeddingAsync(textToEmbed);

                HangmanWordRecord record = new()
                {
                    Key = entry.Key,
                    //Value = new Value
                    //{
                    Word = entry.Word,
                    //Metadata = new Metadata
                    //{
                    Category = entry.Category,
                    Language = entry.Language,
                    //},
                    Embedding = vector
                    //}
                };
                records.Add(record);

                await collection.UpsertWorkaround(record);
            }

            //await collection.UpsertAsync(records); // This currently isn't working
            Console.WriteLine($"Successfully stored {records.Count} words.");

            // Perform search and return words array
            var queryEmbedding = await embedder.GenerateEmbeddingAsync(queryText);

            var searchResults = collection.SearchAsync(
                            queryEmbedding,
                            4,
                            searchOptions
                        );

            if (searchResults == null)
            {
                return null;
            }

            var matchingWords = (await searchResults.ToListAsync())
                .Select(r => r.Record.Word);

            return matchingWords?.ToArray();
        }
    }
}


internal static class SqliteCollectionExtension
{
    // Manually insert the records that the Connector should do when it works :X
    async internal static Task UpsertWorkaround(this SqliteCollection<int, HangmanWordRecord> source, HangmanWordRecord record)
    {
        var serializedJson = JsonSerializer.Serialize(record);

        string connectionString = "c:/temp/hangman_vectors.db";
        using var insertConn = new SqliteConnection($"Data Source={connectionString}");
        insertConn.Open();

        using var transaction = insertConn.BeginTransaction();

        var insertCmd = insertConn.CreateCommand();
        insertCmd.CommandText = @"
    INSERT OR REPLACE INTO words (key, json, word, language, category, length)
    VALUES (@key, @json, @word, @language, @category, @length)
    ";

        insertCmd.Parameters.AddWithValue("@key", record.Key);
        insertCmd.Parameters.AddWithValue("@json", serializedJson);
        insertCmd.Parameters.AddWithValue("@word", record.Word ?? "");
        insertCmd.Parameters.AddWithValue("@language", record.Language ?? "");
        insertCmd.Parameters.AddWithValue("@category", record.Category ?? "");
        insertCmd.Parameters.AddWithValue("@length", record.Length);

        var insertedCount = insertCmd.ExecuteNonQuery();

        byte[] vectorBytes = record.Embedding.ToArray()
        .SelectMany(f => BitConverter.GetBytes(f))
        .ToArray();

        var vecCmd = insertConn.CreateCommand();
        vecCmd.CommandText = @"
            INSERT OR REPLACE INTO vec_words (key, embedding)
            VALUES (@rowid, @embedding)
        ";        

        vecCmd.Parameters.AddWithValue("@rowid", record.Key);
        vecCmd.Parameters.AddWithValue("@embedding", vectorBytes);

        vecCmd.ExecuteNonQuery();

        var chunkCmd2 = insertConn.CreateCommand();
        chunkCmd2.CommandText = @"SELECT count(*) FROM vec_words;";
        var res = await chunkCmd2.ExecuteReaderAsync();

        transaction.Commit();

        insertConn.Close();        
    }

    internal static void ValidateUpsert(SqliteConnection conn)
    {
        //using var conn = new SqliteConnection("Data Source=c:/temp/hangman_vectors.db");
        //conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM words LIMIT 1;";
        var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Console.WriteLine($"key: {reader["key"]}, json: {reader["json"]}, word: {reader["word"]}");
        }
        conn.Close();
    }
}



public record EmbeddingFile
{
    [JsonPropertyName("collectionName")]
    public string CollectionName { get; set; }
    [JsonPropertyName("records")]
    public HangmanWordRecord[] Records {  get; set; }
}


public record HangmanWordRecord
{
    [VectorStoreKey]
    [JsonPropertyName("key")]
    public int Key { get; set; }

    [VectorStoreData]
    [JsonPropertyName("word")]
    public string Word { get; init; } = string.Empty;

    [VectorStoreVector(768)]
    [JsonPropertyName("embedding")]
    public ReadOnlyMemory<float> Embedding { get; init; } = default;

    [VectorStoreData]
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [VectorStoreData]
    [JsonPropertyName("language")]
    public string Language { get; init; } = string.Empty;

    [VectorStoreData]
    [JsonPropertyName("length")]
    public int Length
    {
        get
        {
            return Word?.Length ?? 0;
        }
        set
        {
        }
    }
}