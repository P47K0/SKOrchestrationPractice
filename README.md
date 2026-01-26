This starter is for a Multi Agent setup with Semantic Kernel.  
For more details on my journey to make my first AI project and tips see:  
[My LinkedIn article: "Multi agent Hangman game with Semantic Kernel Practice project"](https://www.linkedin.com/pulse/multi-agent-hangman-game-semantic-kernel-practice-patrick-koorevaar-1mvef/?trackingId=CU8au4NVTjmTsNrn8MXoVw%3D%3D)

### Local Inference Setup (Intel GPU Optimized via IPEX-LLM — Recommended for Windows!)
This example uses a portable Ollama build with **IPEX-LLM** integration for fast, stable local inference on Intel GPUs (Arc / Xe Graphics).

**Why this build?**
- Standard Ollama can lag (> 60s responses on 32B models) or reboot frequently without full optimization.
- IPEX-LLM version: Smooth, high-token-rate runs on Intel hardware — no NVIDIA needed!

**Tested on**: Windows with Intel Arc + latest drivers.

**Quick Setup Steps** (Portable — No Full Install Needed):
1. **Update Intel Drivers** (essential for stability!):  
   Download latest from [intel.com/support](https://www.intel.com/content/www/us/en/support/products/80939/graphics.html) (search your GPU model).

2. **Download IPEX-LLM Ollama Portable Zip**:  
   Get the latest Windows version from Intel's guide:  
   [GitHub Quickstart - Ollama Portable Zip](https://github.com/intel/ipex-llm/blob/main/docs/mddocs/Quickstart/ollama_portable_zip_quickstart.md)  
   (Your ollama-ipex-llm-2.2.0-win is solid; upgrade if needed for newer features.)
   [Or here](https://github.com/ipex-llm/ipex-llm/releases/tag/v2.2.0)

4. **Unzip & Start**:  
   - Extract the zip to a folder.  
   - Run `start-ollama.bat` (or equivalent in latest zip).  

5. **Pull a Model**:  
   In a command prompt:  
   `ollama pull qwen2.5:32b-instruct-q5_K_M`  
   (Quantized GGUF models recommended for max speed.)

6. **Run the Server**:  
   Ollama serves automatically on startup — accessible at `http://localhost:11434`.

NVIDIA/Standard Ollama users: Use official Ollama download + CUDA for equivalent performance.

Feedback welcome if issues on your hardware!


## v1.1: Added local RAG search (with workaround)
The word pool is now retreived via local RAG. The words (in words.json) are placed in a Sqlite DB with vectors so a semantic search can be done.
The path of the SqliteDb can be set in Program.cs: SqlitePath it is currently set to 'c:/temp/hangman_vectors.db' (will move all settings to appsettings later)
Currently the first 4 fruits with 5 letters are fetched.
In order to do create the vectors, it is needed to deploy an embeddings model. I tested it with nomic-embed-text, you have to do:
`ollama pull nomic-embed-text`

## Workaround for Semantic Kernel vector store preview bugs

The official `UpsertAsync` method in `SqliteVecVectorStore` (and other preview connectors) failed to reliably write vector data to the chunk tables (`vec_words_vector_chunks00` stayed empty), even though metadata was inserted correctly and `GetAsync(key)` returned full records. This caused all vector searches to return 0 results.

### Root issues encountered
- InMemoryVectorStore: "Collection does not exist" despite `GetCollection` + dummy upsert
- SqliteVectorStore: NOT NULL constraint failed on `json` column during upsert
- SqliteVec: Vector chunks never populated (extension loaded, `vec_version()` worked, but inserts skipped)

### Solution: Manual raw-SQL upsert + connector search
- Metadata (key, json, word, category, language, length) inserted via raw `INSERT OR REPLACE` into `words` table
- Vector inserted manually into `vec_words` virtual table (or chunks) using BLOB parameter
- Search still uses `VectorizedSearchAsync` (once chunks are populated)
- Post-filter on `Category.Contains("fruit")` + `Length == 5` to guarantee relevant fruits

### Code snippet (simplified manual upsert)
```csharp
internal static void UpsertWorkaround(this SqliteCollection<int, HangmanWordRecord> source, HangmanWordRecord record)
{
    string connectionString = $"Data Source={Program.SqlitePath}";
    using var insertConn = new SqliteConnection(connectionString);
    conn.Open();

    // Metadata
    var metaCmd = conn.CreateCommand();
    metaCmd.CommandText = @"
        INSERT OR REPLACE INTO words (key, json, word, language, category, length)
        VALUES (@key, @json, @word, @language, @category, @length)
    ";
    metaCmd.Parameters.AddWithValue("@key", record.Key);
    metaCmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(record));
    metaCmd.Parameters.AddWithValue("@word", record.Word ?? "");
    metaCmd.Parameters.AddWithValue("@language", record.Language ?? "");
    metaCmd.Parameters.AddWithValue("@category", record.Category ?? "");
    metaCmd.Parameters.AddWithValue("@length", record.Length);
    metaCmd.ExecuteNonQuery();

    // Vector (BLOB)
    var vecCmd = conn.CreateCommand();
    vecCmd.CommandText = @"
        INSERT OR REPLACE INTO vec_words (key, embedding)
        VALUES (@id, @embedding)
    ";
    var vectorBytes = record.Embedding.ToArray()
        .SelectMany(BitConverter.GetBytes)
        .ToArray();
    vecCmd.Parameters.AddWithValue("@id", record.Key);
    vecCmd.Parameters.AddWithValue("@embedding", vectorBytes);
    vecCmd.ExecuteNonQuery();

    conn.Close();
}

## Dependencies and Setup Notes

### SQLite vec0 Extension (Vector Search)

This project uses the experimental [sqlite-vec](https://github.com/asg017/sqlite-vec) extension (v0.x alpha series) for vector similarity search in SQLite.

- The Windows x64 native library `vec0.dll` is **included directly in the repository**.
- It is added to the project with:
  - Build Action: None
  - Copy to Output Directory: Copy always

This ensures the DLL ends up in the output folder (`bin/Debug/net8.0/` or similar) and is available at runtime.

The extension is loaded explicitly in code:

```csharp
connection.EnableExtensions(true);
connection.LoadExtension("extensions/vec0.dll");

**Platform notes**
- Works out-of-the-box on Windows x64.
- On Linux/macOS/other platforms, the bundled Windows vec0.dll will not work. Replace it with the appropriate native file:
1. Go to the release page [releases](https://github.com/asg017/sqlite-vec/releases)
   I used this release: [v0.1.7-alpha.2](https://github.com/asg017/sqlite-vec/releases/tag/v0.1.7-alpha.2)
2. Download the appropriate precompiled loadable extension (many platform-specific assets are available, e.g., vec0.so for Linux, vec0.dylib for macOS).
3. Place it in the output directory.
4. Update the LoadExtension call if the filename differs (e.g., "vec0.so").

**About the official Semantic Kernel SqliteVec connector**
A prerelease connector exists:
`dotnet add package Microsoft.SemanticKernel.Connectors.SqliteVec --prerelease`
It provides a higher-level vector store abstraction and depends on a separate sqlite-vec NuGet package that attempts to supply native assets.However, as of January 2026, real-world usage often still requires the native extension file (vec0.dll on Windows) to be present in the executable directory for reliable loading—similar issues to the manual approach can occur.
For maximum reliability and explicit control (especially in a small project like this Hangman demo), the direct/manual method is used here. Switching to the official connector is possible in the future once the native handling stabilizes across platforms.

**License for the Bundled vec0.dll**
The precompiled vec0.dll included in this repository is from the sqlite-vec project and is dual-licensed under:
- MIT License
- Apache License 2.0

Both are permissive licenses that allow use, modification, and redistribution (including binaries) with minimal requirements (primarily preserving copyright notices and license text).
You may comply with either license.
Full license texts are available in the upstream repository:
- [LICENSE-MIT](https://github.com/asg017/sqlite-vec/blob/main/LICENSE-MIT)
- [LICENSE-APACHE](https://github.com/asg017/sqlite-vec/blob/main/LICENSE-APACHE)

(No additional license obligations are imposed by bundling the binary here beyond those in the original licenses.)

