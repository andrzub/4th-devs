using _02_02_zadanie.Board;
using _02_02_zadanie.Hub;
using _02_02_zadanie.Llm;
using _02_02_zadanie.Mission;
using _02_02_zadanie.Tools;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var describeMode = args.Contains("--describe");
var runMode = args.Contains("--run");
var reset = args.Contains("--reset");

// Offline debug helper: slice a local PNG into tiles to inspect grid detection.
// No network, no LLM calls, no API keys needed.
var sliceIndex = Array.IndexOf(args, "--slice");
if (sliceIndex >= 0)
{
    var slicePath = args.ElementAtOrDefault(sliceIndex + 1)
        ?? throw new ArgumentException("Usage: dotnet run -- --slice <path-to-png>");
    var sliceCacheDir = configuration["Electricity:CacheDirectory"] ?? "board-cache";
    Directory.CreateDirectory(sliceCacheDir);

    var prefix = Path.GetFileNameWithoutExtension(slicePath);
    foreach (var tile in BoardImageSlicer.Slice(await File.ReadAllBytesAsync(slicePath)))
    {
        var outPath = Path.Combine(sliceCacheDir, $"slice_{prefix}_{tile.Label}.png");
        await File.WriteAllBytesAsync(outPath, tile.PngBytes);
        Console.WriteLine(outPath);
    }
    return;
}

if (!describeMode && !runMode)
{
    Console.WriteLine("S02E02 'electricity' — wiring puzzle agent.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- --describe [--reset]   Vision test only: fetch target + board and print");
    Console.WriteLine("                                       their tile descriptions. No rotations, no /verify.");
    Console.WriteLine("  dotnet run -- --run [--reset]        Full agent loop. Every rotation is a /verify request.");
    Console.WriteLine();
    Console.WriteLine("  --reset re-fetches the board with ?reset=1 first (restores the initial layout).");
    return;
}

var openAiSettings = configuration.GetSection("OpenAI").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing 'OpenAI' configuration section.");
var geminiSettings = configuration.GetSection("Gemini").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing 'Gemini' configuration section.");
var aiDevsApiKey = configuration["AI_DevsApiKey"];
if (string.IsNullOrWhiteSpace(aiDevsApiKey))
    throw new InvalidOperationException("Missing 'AI_DevsApiKey' — set it in appsettings.Development.json.");

var hubBaseUrl = configuration["Electricity:HubBaseUrl"] ?? "https://hub.ag3nts.org";
var targetImageUrl = configuration["Electricity:TargetImageUrl"] ?? throw new InvalidOperationException("Missing 'Electricity:TargetImageUrl'.");
var cacheDirectory = configuration["Electricity:CacheDirectory"] ?? "board-cache";

var visionClient = new OpenAiCompatibleLlmClient("Gemini", geminiSettings);
var hub = new HubClient(hubBaseUrl, aiDevsApiKey, "electricity-log.jsonl");
var vision = new TileVisionService(visionClient, cacheDirectory);

var state = new MissionState();
var readBoard = new ReadBoardTool(hub, vision);
var readTarget = new ReadTargetTool(hub, vision, targetImageUrl, cacheDirectory);

if (describeMode)
{
    Console.WriteLine($"Vision provider: Gemini ({geminiSettings.DefaultModel})");
    Console.WriteLine();
    Console.WriteLine(await readTarget.ExecuteAsync("{}"));
    Console.WriteLine(await readBoard.ExecuteAsync(reset ? """{"reset":true}""" : "{}"));
    Console.WriteLine($"Tile crops saved to '{cacheDirectory}' — inspect them if any description looks off.");
    return;
}

// ---------------------------------------------------------------------------
// Agent loop (--run): every rotate_tile call below is a real /verify request.
// ---------------------------------------------------------------------------

var agentClient = new OpenAiCompatibleLlmClient("OpenAI", openAiSettings);
var tools = new List<ITool> { readTarget, readBoard, new RotateTileTool(hub, state) };

Console.WriteLine($"Agent loop: OpenAI ({openAiSettings.DefaultModel}), vision: Gemini ({geminiSettings.DefaultModel})");

if (reset)
{
    Console.WriteLine("Resetting board to initial state...");
    await hub.GetBoardPngAsync(reset: true);
}

var messages = new List<Message>
{
    Message.System(ElectricityAgentPrompt.SystemPrompt),
    Message.User("Solve the puzzle: make the current board match the target schema. Begin.")
};

const int maxIterations = 30;
var finishRefusals = 0;

for (var iteration = 1; iteration <= maxIterations && !state.FlagReceived; iteration++)
{
    Console.WriteLine($"--- Iteration {iteration} ---");
    var response = await agentClient.CompleteAsync(new LlmRequest { Messages = messages, Tools = tools });

    if (response.HasToolCalls)
    {
        messages.Add(Message.AssistantToolCalls(response.ContentRaw, response.ToolCalls));
        foreach (var toolCall in response.ToolCalls)
        {
            var tool = tools.FirstOrDefault(t => t.Name == toolCall.FunctionName);
            var result = tool is null
                ? $"Unknown tool '{toolCall.FunctionName}'."
                : await tool.ExecuteAsync(toolCall.ArgumentsJson);
            messages.Add(Message.ToolResult(toolCall.Id, result));

            if (state.FlagReceived)
                break;
        }
        continue;
    }

    Console.WriteLine(response.Content);
    messages.Add(Message.Assistant(response.Content ?? ""));

    // The model finished without a flag observed in any tool result — that is never
    // a valid end state, so push it back to work (bounded, to avoid burning budget).
    if (++finishRefusals > 3)
    {
        Console.WriteLine("Agent kept finishing without a flag — aborting.");
        break;
    }
    messages.Add(Message.User(
        "No {FLG:...} flag has appeared in any tool result yet, so the puzzle is not solved. " +
        "Re-read the board, find the tiles that still differ from the target and fix them."));
}

Console.WriteLine();
Console.WriteLine($"Rotations sent: {state.RotationCount}");
Console.WriteLine(state.FlagReceived
    ? $"FLAG RECEIVED: {state.Flag}"
    : "No flag received. Check electricity-log.jsonl and board-cache/ to diagnose.");
