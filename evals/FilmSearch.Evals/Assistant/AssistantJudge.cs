using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FilmSearch.AI.Prompts;
using FilmSearch.Evals.Datasets;
using Microsoft.Extensions.AI;

namespace FilmSearch.Evals.Assistant;

/// <summary>Structured output of the judge. <c>comment</c> comes first: the model writes its reasons before the scores.</summary>
internal sealed record JudgeOutput(string Comment, int Relevance, int Explanations, int Tone, int Constraints);

/// <summary>
/// LLM-as-judge (spec 09 §3): the rubric prompt <c>assistant-judge.vN</c> (versioned like the product prompts, embedded in
/// this assembly), the transcript of one case run as the user message, structured output with a JSON Schema from
/// <see cref="JudgeOutput"/>, temperature 0. An invalid answer gets one retry with the error; then the run has no score.
/// <para>
/// Known biases of LLM judges: they prefer longer answers (verbosity bias) and answers of their own model family
/// (self-preference), and they drift between runs. The rubric says not to reward length; a judge of another model than
/// the assistant (<c>--judge-profile</c> / <c>--judge-model</c>) reduces self-preference; <c>--repeat</c> shows the drift.
/// </para>
/// </summary>
public sealed class AssistantJudge
{
    public const string PromptName = "assistant-judge";
    private const int MaxAttempts = 2;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    private readonly IChatClient _chat;
    private readonly PromptTemplate _prompt;
    private readonly JsonElement _schema;

    public AssistantJudge(IChatClient chat, int? promptVersion = null)
    {
        _chat = chat;
        _prompt = new PromptLibrary(typeof(AssistantJudge).Assembly).Get(PromptName, promptVersion);
        _schema = AIJsonUtilities.CreateJsonSchema(
            typeof(JudgeOutput),
            serializerOptions: Json,
            inferenceOptions: new AIJsonSchemaCreateOptions
            {
                TransformOptions = new AIJsonSchemaTransformOptions { RequireAllProperties = true, DisallowAdditionalProperties = true },
            });
    }

    public string PromptId => _prompt.Id;

    public async Task<(JudgeScores? Scores, string? Error)> JudgeAsync(AssistantCase assistantCase, IReadOnlyList<TurnRecord> turns, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, _prompt.Text),
            new(ChatRole.User, Transcript(assistantCase, turns)),
        ];
        var options = new ChatOptions
        {
            Temperature = 0,
            MaxOutputTokens = 600,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(_schema, "judge_scores", "Scores of the conversation by the rubric."),
        };

        var error = "";
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var response = await _chat.GetResponseAsync(messages, options, cancellationToken);
            if (TryParse(response.Text, out var scores, out error))
            {
                return (scores, null);
            }

            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text));
            messages.Add(new ChatMessage(ChatRole.User, $"Your answer is invalid: {error} Answer again with only the corrected JSON object."));
        }

        return (null, error);
    }

    internal static bool TryParse(string text, out JudgeScores? scores, out string error)
    {
        scores = null;
        JudgeOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<JudgeOutput>(StripFence(text), Json);
        }
        catch (JsonException ex)
        {
            error = $"not valid JSON: {ex.Message}";
            return false;
        }

        if (output is null)
        {
            error = "the answer is empty.";
            return false;
        }

        int[] values = [output.Relevance, output.Explanations, output.Tone, output.Constraints];
        if (values.Any(v => v is < 1 or > 5))
        {
            error = "every score must be an integer from 1 to 5.";
            return false;
        }

        scores = new JudgeScores(output.Relevance, output.Explanations, output.Tone, output.Constraints, output.Comment ?? "");
        error = "";
        return true;
    }

    /// <summary>The conversation as the judge reads it: what the customer saw and what the tools returned.</summary>
    public static string Transcript(AssistantCase assistantCase, IReadOnlyList<TurnRecord> turns)
    {
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"Customer's locale: {assistantCase.Locale}");
        if (assistantCase.Setup?.Cart is { Count: > 0 } cart)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"Cart before the conversation: {cart.Count} movie(s) added by the customer.");
        }

        if (assistantCase.Judge is { } note)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"What a good conversation does here: {note}");
        }

        md.AppendLine();
        md.AppendLine("<transcript>");
        foreach (var turn in turns)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"## Turn {turn.Number}");
            md.AppendLine(CultureInfo.InvariantCulture, $"Customer: {turn.User}");
            foreach (var tool in turn.Tools)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"Tool call: {tool.Name} {tool.Args}{(tool.Ok ? "" : " (refused or failed)")}");
            }

            if (turn.Cards.Count > 0)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"Movies returned by tools (cards): {string.Join("; ", turn.Cards.Select(c => $"{c.Title} ({c.Year}) [{string.Join(", ", c.GenreSlugs)}]"))}");
            }

            if (turn.CartUpdated || turn.ConfirmationRequired)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"Cart after the turn: {(turn.CartAfter.Count == 0 ? "empty" : string.Join("; ", turn.CartAfter.Select(l => $"{l.Title} ({l.Offer})")))}");
            }

            if (turn.ConfirmationRequired)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"Checkout button shown to the customer, total {turn.ConfirmationTotal} (computed by the server).");
            }

            md.AppendLine(turn.Error is not null ? $"Assistant: (failed: {turn.Error})" : $"Assistant: {turn.Answer}");
            md.AppendLine();
        }

        md.AppendLine("</transcript>");
        return md.ToString();
    }

    private static string StripFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return start >= 0 && end > start ? trimmed[(start + 1)..end].Trim() : trimmed;
    }
}
