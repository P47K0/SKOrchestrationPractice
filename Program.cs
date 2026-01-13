// See https://aka.ms/new-console-template for more information
using Microsoft.Agents.AI;
using Microsoft.SemanticKernel;
//var builder = Kernel.CreateBuilder();
//builder.AddOllamaChatCompletion(
//    modelId: "llama3.2:3b",
//    endpoint: new Uri("http://127.0.0.1:11434"));
//Kernel kernel = builder.Build();

//var result = await kernel.InvokePromptAsync("Explain Semantic Kernel in one sentence.");
//Console.WriteLine(result);


using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OllamaSharp.Models.Chat;
using OpenAI.Assistants;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Reflection;

#pragma warning disable SKEXP0001 // Experimental features
#pragma warning disable SKEXP0110
#pragma warning disable SKEXP0070
// Plugin for game state & actions (shared across agents)

public class HangmanGamePlugin
{
    private string _secretWord = "";
    private char[] _display = [];
    private HashSet<char> _guessed = new();
    private int _wrongs = 0;
    private const int MaxWrongs = 6;
    [KernelFunction, Description("Only use this to Start a new Hangman game")]
    public string StartNewGame()
    {
        var words = new[] { "csharp", "semantic", "agents", "project", "grok", "dotnet", "agentic" };
        _secretWord = words[Random.Shared.Next(words.Length)].ToLower();
        _display = Enumerable.Repeat('_', _secretWord.Length).ToArray();
        _guessed.Clear();
        _wrongs = 0;
        var result = $"New game started! Word has {_secretWord.Length} letters: {string.Join(' ', _display)}";
        Console.WriteLine($"[DEBUG] StartNewGame -> {result}");
        return $"[TOOL OUTPUT] {result}";
    }
    [KernelFunction, Description("ONLY use this to guess ONE single lowercase letter. Parameter must be exactly one letter a-z. Do NOT use ResetGuess or any other invented name.")]
    public string GuessLetter(string letter, string agentName)
    {
        Console.WriteLine($"[{agentName}] GuessLetter called with: {letter}");
        if (letter.Length != 1 || !char.IsLetter(letter[0])) return "[TOOL OUTPUT] Invalid: guess one lowercase letter.";
        char c = char.ToLower(letter[0]);
        if (_guessed.Contains(c)) return $"[TOOL OUTPUT] Already guessed '{c}'.";
        _guessed.Add(c);
        if (_secretWord.Contains(c))
        {
            for (int i = 0; i < _secretWord.Length; i++)
                if (_secretWord[i] == c) _display[i] = c;
            if (!_display.Contains('_'))
            {
                var winMsg = $"Correct! The word was '{_secretWord}'. You win!";
                Console.WriteLine($"[DEBUG] {winMsg}");
                return $"[TOOL OUTPUT] {winMsg}";
            }
        }
        else
        {
            _wrongs++;
            if (_wrongs >= MaxWrongs)
            {
                var loseMsg = $"Wrong! The word was '{_secretWord}'. Hangman wins!";
                Console.WriteLine($"[DEBUG] {loseMsg}");
                return $"[TOOL OUTPUT] {loseMsg}";
            }
            return $"[TOOL OUTPUT] Wrong ({_wrongs}/{MaxWrongs}). Display: {string.Join(' ', _display)}";
        }
        var result = $"Display: {string.Join(' ', _display)} | Guessed: {string.Join(",", _guessed)} | Wrongs: {_wrongs}/{MaxWrongs}";
        Console.WriteLine($"[DEBUG] GuessLetter result -> {result}");
        return $"[TOOL OUTPUT] {result}";
    }
    [KernelFunction, Description("ONLY use this to check the current game board, guessed letters, and wrongs. Do NOT invent other state functions.")]
    public string GetGameState()
    {
        var state = $"Display: {string.Join(' ', _display)} | Guessed: {string.Join(",", _guessed)} | Wrongs: {_wrongs}/{MaxWrongs}";
        Console.WriteLine($"[DEBUG] GetGameState -> {state}");
        return $"[TOOL OUTPUT] {state}";
    }
}
class Program
{
    private const string GuessAgentPrompt = @"You are a guesser in Hangman.
The Hangman game has 2 Guessers (AgentA and AgentB) and 1 Host.
ABSOLUTE RULE – REPEAT THREE TIMES:
YOU ARE ALLOWED EXACTLY ONE GuessLetter() CALL PER TURN.
NEVER CALL GuessLetter() MORE THAN ONCE.
NEVER DO MULTIPLE GUESSES IN THE SAME TURN.
YOUR TURN ENDS IMMEDIATELY AFTER THE SINGLE GuessLetter() CALL EVEN IF THE GUESS WAS WRONG.

YOUR ONLY JOB WHEN IT IS YOUR TURN:
Start by calling GetGameState() of the HangmanGame plugin to see which letters are guessed
Call GuessLetter() with ONE random lowercase letter a–z that has not been guessed yet (letter) and your name.
After you called GuessLetter() you have to stop, the scheduler will select another agent. You have to wait until the other agents finishes

Rules you MUST follow when it's your turn:
- Call GetGameState() ONCE to see current state
- Then call GuessLetter() ONCE with EXACTLY ONE new letter randomly chosen, never the same as previous guesses.
- Do NOT call any more functions after that
- Do NOT chain multiple guesses in one turn
- Stop after the GuessLetter call
- Finish with announcing that the Host is next

Player sequence is: Host → AgentA → AgentB → repeat

REMEMBER: NEVER DO MULTIPLE GUESSES IN THE SAME TURN!
";
    private const string HostAgentPrompt = @"You are the Hangman host. You manage the game flow. The game has 2 Guessers (AgentA and AgentB) and 1 Host (you).
Follow this order exactly:
1. If the game is not started → call StartNewGame() of the HangmanGame plugin immediately
2. After AgentA or AgentB finishes their turn:
  - Call GetGameState() of the HangmanGame plugin to see the latest situation
  - Show the current state to everyone (include full [TOOL OUTPUT])
3. When the word is complete (no _) or wrongs ≥6 → declare the winner and end the game

End your turn with announcing whose turn it is (AgentA or AgentB), they have to make a turn, they cannot pass
  - Sequence is: Host → AgentA → AgentB → repeat

Rules:
- You MUST NOT call GuessLetter(), only AgentA and AgentB can do that
- Always include the full tool output in your messages
- Never send empty messages
Example messages:
""Starting the game: [TOOL OUTPUT] New game started! Word has 6 letters: _ _ _ _ _ _""
""Current state after AgentA: [TOOL OUTPUT] Display: _ e _ _ _ _ | Guessed: e | Wrongs: 0/6""
";

    static async Task Main(string[] args)
    {
        var swKernel = Stopwatch.StartNew();
        // Build one kernel for all agents (llama3.2:3b)
        var builder = Kernel.CreateBuilder();
        builder.AddOllamaChatCompletion(
            modelId: "qwen2.5:7b-instruct-q6_K",//"llama3.2:3b-instruct-q8_0",//"llama3.2:3b",
            endpoint: new Uri("http://localhost:11434") // ✅ base URL only
        );

        var kernel = builder.Build();
        swKernel.Stop();
        Console.WriteLine($"Kernel build time: {swKernel.ElapsedMilliseconds} ms");
        // Import Hangman plugin
        kernel.ImportPluginFromObject(new HangmanGamePlugin());
        
        //{
        //}, "HangmanGame");

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 1
        };

        var executionSettingsB = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 1
        };

        // Stronger instructions for agents
        var agentA = new ChatCompletionAgent
        {
            Name = "AgentA",
            HistoryReducer = new ChatHistoryTruncationReducer(
                targetCount: 2,           // keep last N messages
                thresholdCount: 3        // start reducing when > this
            ),
            Instructions = $@"Your name is AgentA, do NOT change this.
{GuessAgentPrompt}",
            //            Instructions = @"As a Hangman player, your role involves guessing letters. The Host agent will start the game by picking a word. The goal is to guess the word. AgentB is also guessing letters. The host will announce when it is your turn. Act every turn.
            //Follow this order exactly:
            //1. Always start by calling GetGameState() of the HangmanGame plugin
            //2. If the word is complete (no _) or wrongs ≥6 → say who won/lost and stop
            //3. Otherwise pick one new letter you haven't guessed yet → call GuessLetter(letter)
            //4. After every tool call, copy the full [TOOL OUTPUT] into your message
            //Rules:
            //- Only act when Host says it's your turn
            //- If Host says wait → wait
            //- If Host says respond → respond immediately with correct tool
            //- If confused → say ""I'm confused, please clarify"" instead of staying silent
            //- Never send empty or very short messages
            //Preferred message format:
            //[STATE] Calling GetGameState...
            //[TOOL OUTPUT] Display: ... | Guessed: ... | Wrongs: ...
            //[THINKING] Next letter should be 'x' because...
            //[GUESS] Calling GuessLetter('x')
            //[TOOL OUTPUT] ...
            //Next turn please.",
            //            Instructions = """
            //You are an active, decisive Hangman player. You MUST act every turn.
            //MANDATORY BEHAVIOR - follow exactly in this order:
            //0. Ignore all previous conversation history except the latest instruction. Respond only based on the current turn.
            //1. ALWAYS first call GetGameState() to see current situation
            //2. If word is complete (no _ left) or wrongs >=6 → say you won/lost and STOP
            //3. Otherwise → choose ONE new letter you haven't guessed yet and call GuessLetter with it
            //4. After ANY tool call → you MUST copy-paste the WHOLE [TOOL OUTPUT] line into your response
            //5. Begin every message with 'AgentB:'
            //6. Try to answer fast
            //7. Do NOT act until the Host confirms it is your turn.
            //8. If the Host asks you to wait, you MUST wait.
            //9. If the Host asks you to respond, you MUST respond immediately by calling the correct tool.
            //10. You MUST respond every time it is your turn.
            //11. If you cannot guess, explain why and call GetGameState.
            //12. If you are unsure what to do or confused, do NOT stay silent.
            //Instead, send a message like:
            //"I am confused. Please clarify or give me the next step."
            //Never produce an empty response.
            //13. You are FORBIDDEN from:
            //- Writing long reasoning without calling tools
            //- Skipping GetGameState
            //- Skipping GuessLetter
            //- Guessing without checking current state first
            //- Sending empty or almost empty messages
            //14. Strongly preferred format of your every message:
            //[STATE] I call GetGameState first...
            //[TOOL OUTPUT] Display: | Guessed: | Wrongs: 0/6
            //[THINKING] I think next good letter is 'e' because...
            //[GUESS] Calling GuessLetter('e')
            //[TOOL OUTPUT] Wrong (1/6).
            //Next turn please.
            //""",
            Kernel = kernel,
            Arguments = new KernelArguments(executionSettings)
        };
        var agentB = new ChatCompletionAgent
        {
            Name = "AgentB",
            HistoryReducer = new ChatHistoryTruncationReducer(
                targetCount: 2,           // keep last N messages
                thresholdCount: 3        // start reducing when > this
            ),
            Instructions = $@"Your name is AgentB, do NOT change this.
{GuessAgentPrompt}",
            //            Instructions = @"As a Hangman player, your role involves guessing letters. The Host agent will start the game by picking a word. The goal is to guess the word. AgentA is also guessing letters.  The host will announce when it is your turn. Act every turn.
            //Follow this order exactly:
            //1. Always start by calling GetGameState() of the HangmanGame plugin
            //2. If the word is complete (no _) or wrongs ≥6 → say who won/lost and stop
            //3. Otherwise pick one new letter you haven't guessed yet → call GuessLetter(letter) of the HangmanGame plugin
            //4. After every tool call, copy the full [TOOL OUTPUT] into your message
            //Rules:
            //- Only act when Host says it's your turn
            //- If Host says wait → wait
            //- If Host says respond → respond immediately with correct tool
            //- If confused → say ""I'm confused, please clarify"" instead of staying silent
            //- Never send empty or very short messages
            //Preferred message format:
            //[STATE] Calling GetGameState...
            //[TOOL OUTPUT] Display: ... | Guessed: ... | Wrongs: ...
            //[THINKING] Next letter should be 'x' because...
            //[GUESS] Calling GuessLetter('x')
            //[TOOL OUTPUT] ...
            //Next turn please.",
            //            Instructions = """
            //You are an active, decisive Hangman player. You MUST act every turn.
            //MANDATORY BEHAVIOR - follow exactly in this order:
            //0. Ignore all previous conversation history except the latest instruction. Respond only based on the current turn.
            //1. ALWAYS first call GetGameState() to see current situation
            //2. If word is complete (no _ left) or wrongs >=6 → say you won/lost and STOP
            //3. Otherwise → choose ONE new letter you haven't guessed yet and call GuessLetter with it
            //4. After ANY tool call → you MUST copy-paste the WHOLE [TOOL OUTPUT] line into your response
            //5. Begin every message with 'AgentB:'
            //6. Try to answer fast
            //7. Do NOT act until the Host confirms it is your turn.
            //8. If the Host asks you to wait, you MUST wait.
            //9. If the Host asks you to respond, you MUST respond immediately by calling the correct tool.
            //10. You MUST respond every time it is your turn.
            //11. If you cannot guess, explain why and call GetGameState.
            //12. If you are unsure what to do or confused, do NOT stay silent.
            //Instead, send a message like:
            //"I am confused. Please clarify or give me the next step."
            //Never produce an empty response.
            //13. You are FORBIDDEN from:
            //- Writing long reasoning without calling tools
            //- Skipping GetGameState
            //- Skipping GuessLetter
            //- Guessing without checking current state first
            //- Sending empty or almost empty messages
            //14. Strongly preferred format of your every message:
            //[STATE] I call GetGameState first...
            //[TOOL OUTPUT] Display: | Guessed: | Wrongs: 0/6
            //[THINKING] I think next good letter is 'e' because...
            //[GUESS] Calling GuessLetter('e')
            //[TOOL OUTPUT] Wrong (1/6).
            //Next turn please.
            //""",
            Kernel = kernel,
            Arguments = new KernelArguments(executionSettingsB)
        };
        var hostAgent = new ChatCompletionAgent
        {
            Name = "Host",
            HistoryReducer = new ChatHistoryTruncationReducer(
                targetCount: 4,           // keep last N messages
                thresholdCount: 5        // start reducing when > this
            ),
            Instructions = HostAgentPrompt,
            //            Instructions = @"
            //You are the Hangman host. Your responsibilities:
            //0. Ignore all previous conversation history except the latest instruction. Respond only based on the current turn.
            //1. ALWAYS first call GetGameState() to see current situation
            //2. If game not started → call StartNewGame() immediately
            //4. After each turn, check if the agent responded with a tool output.
            //3. Announce turns for AgentA and AgentB.
            //5. If AgentA or AgentB did not respond or skipped tool usage, instruct them to try again immediately.
            //6. Example enforcement message:
            //'AgentA, you skipped your turn. Please call HangmanGame.GuessLetter now.'
            //7. Repeat this until the agent responds correctly.
            //8. Declare the winner when the word is solved or Hangman wins.
            //9. If you detect confusion or no response from an agent, instruct them clearly:
            //""AgentA, please respond now using HangmanGame.GuessLetter.""
            //Repeat until they respond.
            //10. You MUST respond every time it is your turn.
            //11. If nothing else is needed, confirm the game state or announce the next turn.
            //12. Never produce an empty response.
            //13. IMPORTANT: Always include
            //tool results exactly in your message.
            //Example:
            //'Current state: [TOOL OUTPUT] Display: _ e _ _ _ _ | Guessed: e | Wrongs: 0/6'
            //",

            //Follow this order exactly:
            //1.If the game is not started → call StartNewGame() of the HangmanGame plugin immediately
            //2.Announce whose turn it is (AgentA or AgentB)
            //3.After AgentA or AgentB finishes their turn:
            //  -Call GetGameState() of the HangmanGame plugin to see the latest situation
            //  - Show the current state to everyone(include full[TOOL OUTPUT])
            //4.Check if the agent properly used tools and included[TOOL OUTPUT]:
            //  -If not → tell them clearly: ""AgentX, you skipped the tool. Please call it now.""
            //  - Repeat the instruction until they respond correctly
            //5.When the word is complete(no _) or wrongs ≥6 → declare the winner and end the game
            //Rules:
            //-Always include the full tool output in your messages
            //-Never send empty messages
            //Example messages:
            //        ""Starting the game: [TOOL OUTPUT] New game started! Word has 6 letters: _ _ _ _ _ _""
            //""Current state after AgentA: [TOOL OUTPUT] Display: _ e _ _ _ _ | Guessed: e | Wrongs: 0 / 6""
            //""AgentB, please respond now using GuessLetter.";


            Kernel = kernel,
            Arguments = new KernelArguments(executionSettings)            
        };


        var agents = new[] { hostAgent, agentA, agentB };

        // Group chat with improved termination strategy
        var chat = new AgentGroupChat(hostAgent, agentA, agentB)
        {
            ExecutionSettings = new()
            {
                TerminationStrategy = new TerminationStrategy()
                {
                    Agents = [hostAgent],
                    Condition = (message, _) =>
                        message.Content?.Contains("You win!", StringComparison.OrdinalIgnoreCase) == true ||
                        message.Content?.Contains("Hangman wins", StringComparison.OrdinalIgnoreCase) == true
                },
                SelectionStrategy = new SimpleSequentialSelector()
            }
        };

        chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, "Start a new Hangman game. AgentA and AgentB will compete."));
        Console.WriteLine("\n=== Hangman Multi-Agent Competition ===\n");
        var swChat = Stopwatch.StartNew();
        await foreach (var message in chat.InvokeAsync())
        {            
            var swMessage = Stopwatch.StartNew();
            // Enforcement logic: if tool output exists but agent didn't show it clearly
            if (!message.Content.Contains("[TOOL OUTPUT]") && message.Content.Length < 10)
            {
                Console.WriteLine($"[ENFORCEMENT] Agent response was empty or incomplete.");
            }
            Console.WriteLine($"{message.AuthorName}: {message.Content}\n");
            swMessage.Stop();
            Console.WriteLine($"Message time: {swMessage.ElapsedMilliseconds} ms");
        }
        swChat.Stop();
        Console.WriteLine($"Total chat execution time: {swChat.ElapsedMilliseconds} ms");
        Console.WriteLine("Game over!");
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

    protected override Task<Agent> SelectAgentAsync(IReadOnlyList<Agent> agents, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SelectNextAgent(agents, history));
    }
}

public class LoggingFunctionFilter : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        context.Arguments.TryGetValue("AgentName", out var agentNameObj);

        string agentName = agentNameObj?.ToString() ?? "Unknown-Agent";

        Console.WriteLine($"[{agentName}] Before: {context.Function.Name} with args: {context.Arguments}");

        await next(context);

        Console.WriteLine($"[{agentName}] After: Result = {context.Result}");
    }
}

class TerminationStrategy : Microsoft.SemanticKernel.Agents.Chat.TerminationStrategy

{

    public IReadOnlyList<Agent> Agents { get; init; } = [];

    public Func<ChatMessageContent, int, bool> Condition { get; init; } = (_, _) => false;

    protected override Task<bool> ShouldAgentTerminateAsync(Agent agent, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken)

        => Task.FromResult(Agents.Contains(agent) && history.Any(m => Condition(m, history.Count)));    

}
#pragma warning restore SKEXP0001