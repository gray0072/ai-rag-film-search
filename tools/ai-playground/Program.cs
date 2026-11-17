using FilmSearch.AI.Providers;
using FilmSearch.AiPlayground;
using FilmSearch.AiPlayground.Benchmark;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// AI playground (I05). Uses the same AI section as the API (src/FilmSearch.Api/appsettings*.json + API user secrets),
// so what works here works in the API. See tools/ai-playground/README.md.
//
//   dotnet run --project tools/ai-playground -- <command> ["text"] [options]

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage.Text);
    return 0;
}

var command = args[0];
var text = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
var options = args.Skip(text is null ? 1 : 2).ToArray();

var repositoryRoot = RepositoryRoot.Find();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    EnvironmentName = Environments.Development,
    ContentRootPath = Path.Combine(repositoryRoot, "src", "FilmSearch.Api"),
    ApplicationName = typeof(Program).Assembly.GetName().Name,
});

// Quiet console: only warnings, plus the raw HTTP log when --raw is given.
var raw = options.Contains("--raw");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Logging:LogLevel:Default"] = "Warning",
    ["Logging:LogLevel:FilmSearch.AI.Diagnostics"] = raw ? "Information" : "Warning",
    ["AI:LogRawHttp"] = raw ? "true" : "false",
    ["AI:HealthChecksEnabled"] = "false",
});
builder.Configuration.AddCommandLine(options.Where(o => o != "--raw").ToArray(), new Dictionary<string, string>
{
    ["--chat-profile"] = "AI:ActiveChatProfile",
    ["--embedding-profile"] = "AI:ActiveEmbeddingProfile",
    ["--chat-model"] = "Playground:ChatModel",
    ["--reasoning"] = "Playground:Reasoning",
    ["--embedding-model"] = "Playground:EmbeddingModel",
    ["--query-prefix"] = "Playground:QueryPrefix",
    ["--document-prefix"] = "Playground:DocumentPrefix",
    ["--out"] = "Playground:Out",
});
ApplyModelOverrides(builder.Configuration);
builder.AddAiProviders();

using var host = builder.Build();
var ai = host.Services.GetRequiredService<IOptions<AiOptions>>().Value;
var chatProfile = ai.GetChatProfile(ai.ActiveChatProfile);
var embeddingProfile = ai.GetEmbeddingProfile(ai.ActiveEmbeddingProfile);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
var ct = cts.Token;

var chat = host.Services.GetRequiredService<IChatClient>();
var embeddings = host.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
var outPath = builder.Configuration["Playground:Out"];

Console.Error.WriteLine($"chat: {ai.ActiveChatProfile} → {chatProfile.Model} @ {chatProfile.Endpoint}");
Console.Error.WriteLine($"embeddings: {ai.ActiveEmbeddingProfile} → {embeddingProfile.Model} @ {embeddingProfile.Endpoint}");

switch (command)
{
    case "chat":
        await Demos.ChatAsync(chat, Require(text), ct);
        break;
    case "stream":
        await Demos.StreamAsync(chat, Require(text), ct);
        break;
    case "tools":
        await Demos.ToolsAsync(chat, Require(text), ct);
        break;
    case "json":
        await Demos.JsonAsync(chat, Require(text), ct);
        break;
    case "embed":
        await Demos.EmbedAsync(embeddings, embeddingProfile, ct);
        break;
    case "bench-chat":
        await Report.WriteAsync(await new ChatBenchmark(chat, chatProfile.Model, chatProfile.ReasoningEffort?.ToString() ?? "default", ai.FindPricing(chatProfile.Model)).RunAsync(ct), outPath, ct);
        break;
    case "bench-embed":
        await Report.WriteAsync(await new EmbeddingBenchmark(embeddings, embeddingProfile).RunAsync(ct), outPath, ct);
        break;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'.");
        Console.WriteLine(Usage.Text);
        return 1;
}

return 0;

static string Require(string? text) =>
    text ?? throw new ArgumentException("This command needs a text argument, e.g. chat \"Recommend a sci-fi movie\".");

// --chat-model / --embedding-model / prefixes change the active profile, so any LM Studio model can be benchmarked
// without editing appsettings.
static void ApplyModelOverrides(ConfigurationManager configuration)
{
    var overrides = new Dictionary<string, string?>();
    var chatProfile = configuration["AI:ActiveChatProfile"];
    var embeddingProfile = configuration["AI:ActiveEmbeddingProfile"];

    if (configuration["Playground:ChatModel"] is { } chatModel)
    {
        overrides[$"AI:ChatProfiles:{chatProfile}:Model"] = chatModel;
    }

    if (configuration["Playground:Reasoning"] is { } reasoning)
    {
        overrides[$"AI:ChatProfiles:{chatProfile}:ReasoningEffort"] = reasoning;
    }

    if (configuration["Playground:EmbeddingModel"] is { } embeddingModel)
    {
        overrides[$"AI:EmbeddingProfiles:{embeddingProfile}:Model"] = embeddingModel;
    }

    if (configuration["Playground:QueryPrefix"] is { } queryPrefix)
    {
        overrides[$"AI:EmbeddingProfiles:{embeddingProfile}:QueryPrefix"] = queryPrefix;
    }

    if (configuration["Playground:DocumentPrefix"] is { } documentPrefix)
    {
        overrides[$"AI:EmbeddingProfiles:{embeddingProfile}:DocumentPrefix"] = documentPrefix;
    }

    configuration.AddInMemoryCollection(overrides);
}

internal static class Usage
{
    public const string Text = """
        FilmSearch AI playground (I05)

        Usage: dotnet run --project tools/ai-playground -- <command> ["text"] [options]

        Commands:
          chat "text"     one request, prints the answer and token usage
          stream "text"   streaming answer, prints time to first token and tokens/s
          tools "text"    tool calling with a hand-written loop over fake catalog tools
          json "text"     structured output: extracts a SearchIntent (JSON Schema) from the text
          embed           cosine similarity of en/ru/sv phrases
          bench-chat      model benchmark: tool calls, JSON output, ru/sv answers, speed
          bench-embed     embedding benchmark: cross-lingual retrieval and translation pairs

        Options:
          --chat-profile <name>       chat profile (default AI:ActiveChatProfile), e.g. openrouter
          --chat-model <id>           override the model of the chat profile
          --reasoning <effort>        override the reasoning effort: None, Low, Medium, High
          --embedding-profile <name>  embedding profile
          --embedding-model <id>      override the model of the embedding profile
          --query-prefix <text>       override the query prefix of the embedding profile
          --document-prefix <text>    override the document prefix of the embedding profile
          --out <file>                write the benchmark report (markdown) to a file
          --raw                       log raw HTTP requests and responses
          --AI:<key>=<value>          any other AI setting
        """;
}

internal static class RepositoryRoot
{
    public static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FilmSearch.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (FilmSearch.slnx) not found. Run the tool from the repository.");
    }
}

internal static class Report
{
    public static async Task WriteAsync(string markdown, string? path, CancellationToken ct)
    {
        Console.WriteLine(markdown);
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path, markdown, ct);
            Console.Error.WriteLine($"Report written to {Path.GetFullPath(path)}");
        }
    }
}
