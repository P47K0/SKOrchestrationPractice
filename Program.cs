using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using SKOrchestrationPractice;
using System.ComponentModel;
using System.Diagnostics;

#pragma warning disable SKEXP0001 // Experimental features
#pragma warning disable SKEXP0110
#pragma warning disable SKEXP0070

public class HangmanGamePlugin
{
    private string _secretWord = "";
    private char[] _word = [];
    private readonly HashSet<char> _guessed = [];
    private int _wrongs = 0;
    private const int MaxWrongs = 6;
    private const string QueryTextForWordsPool = "fruit";
    private const string JsonFilePathWordsFile = "words.json";

    async private Task Init(SqliteVectorStore vectorStore)
    {
        var searchOptions = new VectorSearchOptions<HangmanWordRecord>
        {
            Filter = r => r.Length == 5, // Only 5 letter words
            Skip = 0,
            IncludeVectors = false
        };

        var words = await vectorStore.GetWordsAsync(JsonFilePathWordsFile, searchOptions, new Embedder(), QueryTextForWordsPool) ?? ["empty"];
        _secretWord = words[Random.Shared.Next(words.Length)].ToLower();
        _word = Enumerable.Repeat('_', _secretWord.Length).ToArray();
    }

    [KernelFunction, Description("Do not call this, the game is already started")]
    async public Task<string> StartNewGame(SqliteVectorStore vectorStore)
    {
        await Init(vectorStore);
        _guessed.Clear();
        _wrongs = 0;
        var result = $"New game started! Word has {_secretWord.Length} letters: {string.Join(' ', _word)}";
        Console.WriteLine($"[DEBUG] StartNewGame -> {result}");
        return result;
    }

    [KernelFunction, Description("ONLY use this to guess ONE single lowercase letter. Parameter must be exactly one letter a-z. Do NOT use ResetGuess or any other invented name.")]
    public string GuessLetter(string letter, string agentName)
    {
        Console.WriteLine($"[{agentName}] GuessLetter called with: {letter}");
        if (letter.Length != 1 || !char.IsLetter(letter[0])) return "Invalid: guess one lowercase letter.";
        char c = char.ToLower(letter[0]);
        if (_guessed.Contains(c)) return $"Already guessed '{c}'.";
        _guessed.Add(c);
        if (_secretWord.Contains(c))
        {
            for (int i = 0; i < _secretWord.Length; i++)
                if (_secretWord[i] == c) _word[i] = c;
            if (!_word.Contains('_'))
            {
                var winMsg = $"Correct! The word was '{_secretWord}'. You win!";
                //Console.WriteLine($"[DEBUG] {winMsg}");
                return winMsg;
            }
        }
        else
        {
            _wrongs++;
            if (_wrongs >= MaxWrongs)
            {
                var loseMsg = $"Wrong! The word was '{_secretWord}'. Hangman wins!";
                //Console.WriteLine($"[DEBUG] {loseMsg}");
                return loseMsg;
            }
            var result0 = $"Word: {string.Join(' ', _word)} | Guessed: {string.Join(",", _guessed)} | Wrongs: {_wrongs}/{MaxWrongs}";
            //Console.WriteLine($"[DEBUG] GuessLetter result -> {result0}");
            return result0;
        }
        var result = $"Word: {string.Join(' ', _word)} | Guessed: {string.Join(",", _guessed)} | Wrongs: {_wrongs}/{MaxWrongs}";
        //Console.WriteLine($"[DEBUG] GuessLetter result -> {result}");
        return result;
    }

    [KernelFunction, Description("ONLY use this to check the current game board, guessed letters, and wrongs. Do NOT invent other state functions.")]
    public string GetGameState()
    {
        var state = $"Word: {string.Join(' ', _word)} | Guessed: {string.Join(",", _guessed)} | Wrongs: {_wrongs}/{MaxWrongs}";
        //Console.WriteLine($"[DEBUG] GetGameState -> {state}");
        return state;
    }
}
namespace HangmanAgents
{
    class Program
    {
        private const string GuessAgentPromptA = @"You are AgentA in Hangman.
Every turn:
0. Read the most recent message starting with 'Word:'. Extract Word, Guessed, and Wrongs.
1. Call GuessLetter() ONLY ONCE per turn with a random letter which is not after 'Guessed:'.
2. Pass the result from GuessLetter() EXACTLY ('Word: _ _ _' | 'Guessed: a' | 'Wrongs: 0/6') to the chat message
3. End condition:
 - If Word: has '_' → say the result form GuessLetter()
 - If Word: has no '_' → say ""Game Over - You win!""
 - If Wrongs: 6/6 → say ""Game Over - You lose!""

Guess AT MOST ONE letter.
DO NOT add more letters to Guessed, pass the result from GuessLetter() EXACTLY
Please respond clearly and concisely without <tool_call> 
";

        private const string GuessAgentPromptB = @"You are AgentB in Hangman.
Every turn:
0. Read the most recent message starting with 'Word:'. Extract Word, Guessed, and Wrongs.
1. Call GuessLetter() ONLY ONCE per turn with a random letter which is not after 'Guessed:'.
2. Pass the result from GuessLetter() EXACTLY ('Word: _ _ _' | 'Guessed: a' | 'Wrongs: 0/6') to the chat message
3. End condition:
 - If Word: has '_' → say the result form GuessLetter()
 - If Word: has no '_' → say ""Game Over - You win!""
 - If Wrongs: 6/6 → say ""Game Over - You lose!"" 

Guess AT MOST ONE letter.
DO NOT add more letters to Guessed, pass the result from GuessLetter() EXACTLY
Please respond clearly and concisely without <tool_call> 
";
        private const string ModelId = "qwen2.5:32b-instruct-q4_K_M";
        private const string SqlitePath = "c:/temp/hangman_vectors.db";

        static async Task Main(string[] args)
        {
            InitDatabase(out SqliteVectorStore vectorStore, out SqliteConnection conn);

            var swKernel = Stopwatch.StartNew();
            var builder = Kernel.CreateBuilder();
            builder.AddOllamaChatCompletion(
                modelId: ModelId,
                endpoint: new Uri("http://localhost:11434")
            );

            var kernel = builder.Build();
            swKernel.Stop();
            Console.WriteLine($"Kernel build time: {swKernel.ElapsedMilliseconds} ms");

            HangmanGamePlugin game = new();
            await game.StartNewGame(vectorStore);
            kernel.ImportPluginFromObject(game);

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Required(null,
                                                                         true,
                                                                         new FunctionChoiceBehaviorOptions { AllowStrictSchemaAdherence = true }),
                Temperature = 0,
                MaxTokens = 50,
            };

            var executionSettingsB = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Required(null,
                                                                         true,
                                                                         new FunctionChoiceBehaviorOptions { AllowStrictSchemaAdherence = true }),
                Temperature = 0,
                MaxTokens = 50,
            };

            var agentA = new ChatCompletionAgent
            {
                Name = "AgentA",
                HistoryReducer = new ChatHistoryTruncationReducer(targetCount: 1, thresholdCount: 1),
                Instructions = $@"Your name is AgentA, do NOT change this.
{GuessAgentPromptA}",
                Kernel = kernel,
                Arguments = new KernelArguments(executionSettings)
            };
            var agentB = new ChatCompletionAgent
            {
                Name = "AgentB",
                HistoryReducer = new ChatHistoryTruncationReducer(targetCount: 1, thresholdCount: 1),
                Instructions = $@"Your name is AgentB, do NOT change this.
{GuessAgentPromptB}",
                Kernel = kernel,
                Arguments = new KernelArguments(executionSettingsB)
            };

            var agents = new[] { agentA, agentB };
            var chat = new AgentGroupChat(agentA, agentB)
            {
                ExecutionSettings = new()
                {
                    TerminationStrategy = new TerminationStrategy()
                    {
                        Agents = [agentA, agentB],
                        Condition = (message, _) =>
                            message.Content?.Contains("Game over", StringComparison.OrdinalIgnoreCase) == true ||
                            message.Content?.Contains("win", StringComparison.OrdinalIgnoreCase) == true

                    },
                    SelectionStrategy = new SimpleSequentialSelector()
                }
            };

            Console.WriteLine($"\n=== Hangman Multi-Agent Competition ({ModelId}) ===\n");
            string gameState = game.GetGameState();
            chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, gameState));
            Console.WriteLine($"[System] -> '{gameState}'");

            await foreach (var message in chat.InvokeAsync())
            {
                if (string.IsNullOrWhiteSpace(message.Content) || message.Content.Trim().Length < 10)
                {
                    Console.WriteLine("Skipped empty turn.");
                    continue;
                }

                Console.WriteLine($"[{message.AuthorName}] -> '{message.Content}'");
            }

            Console.WriteLine("Game over!");
        }

        private static void InitDatabase(out SqliteVectorStore vectorStore, out SqliteConnection conn)
        {
            string connectionString = $"Data Source={SqlitePath}";

            if (File.Exists(SqlitePath)) File.Delete(SqlitePath);

            vectorStore = new SqliteVectorStore(connectionString);
            conn = new SqliteConnection(connectionString);
            conn.Open();

            conn.EnableExtensions(true);

            conn.LoadExtension("extensions/vec0.dll");

            // 1. Metadata table (for Word, Hint, Category, Length serialized as JSON)
            var cmdMetadata = conn.CreateCommand();
            cmdMetadata.CommandText = @"
CREATE TABLE IF NOT EXISTS words (
    key INTEGER PRIMARY KEY,
    json TEXT NOT NULL,
    word TEXT,
    language TEXT,
    category TEXT,
    length INTEGER
);

CREATE INDEX IF NOT EXISTS idx_words_length ON words(length);
CREATE INDEX IF NOT EXISTS idx_words_word ON words(word);
";
            cmdMetadata.ExecuteNonQuery();

            // 2. Vector virtual table (sqlite-vec style, column named 'embedding')
            var cmdVec = conn.CreateCommand();
            cmdVec.CommandText = @"
CREATE VIRTUAL TABLE IF NOT EXISTS vec_words USING vec0(
    key INTEGER PRIMARY KEY AUTOINCREMENT,
    embedding FLOAT[768]
);
";
            cmdVec.ExecuteNonQuery();

            conn.Close();
        }
    }

    // Custom sequential selector (forces order: AgentA → AgentB → Host → repeat)
    public class SimpleSequentialSelector : SelectionStrategy
    {
        private int _currentIndex = 0;

        public Agent? SelectNextAgent(IReadOnlyList<Agent> agents, IReadOnlyList<ChatMessageContent> history)
        {
            if (agents.Count == 0) return null;

            var agent = agents[_currentIndex % agents.Count];
            _currentIndex++;
            Console.WriteLine($"[SCHEDULER] Selected: {agent.Name}");
            return agent;
        }

        protected override Task<Agent> SelectAgentAsync(IReadOnlyList<Agent> agents,
                                                        IReadOnlyList<ChatMessageContent> history,
                                                        CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SelectNextAgent(agents, history))!;
        }
    }

    class TerminationStrategy : Microsoft.SemanticKernel.Agents.Chat.TerminationStrategy
    {
        public IReadOnlyList<Agent> Agents { get; init; } = [];

        public Func<ChatMessageContent, int, bool> Condition { get; init; } = (_, _) => false;

        protected override Task<bool> ShouldAgentTerminateAsync(Agent agent,
                                                                IReadOnlyList<ChatMessageContent> history,
                                                                CancellationToken cancellationToken)

            => Task.FromResult(Agents.Contains(agent) && history.Any(m => Condition(m, history.Count)));
    }
}

#pragma warning restore SKEXP0001 // Experimental features
#pragma warning restore SKEXP0110
#pragma warning restore SKEXP0070