using System.Diagnostics;

namespace FilmSearch.AI.Telemetry;

/// <summary>
/// Puts prompt-like content on a span when <c>AI:EnableSensitiveData</c> is on (Development). The GenAI attribute is for
/// any OpenTelemetry viewer (Aspire dashboard); the <c>langfuse.observation.*</c> copy makes Langfuse show it as the
/// input / output of the observation. Long texts are cut so one span stays small.
/// </summary>
internal static class TraceContent
{
    private const int MaxLength = 8000;

    public static void SetInput(Activity activity, string attribute, string? text) =>
        Set(activity, attribute, "langfuse.observation.input", text);

    public static void SetOutput(Activity activity, string attribute, string? text) =>
        Set(activity, attribute, "langfuse.observation.output", text);

    /// <summary>Input and output of the whole trace in Langfuse (the user message and the answer of a turn).</summary>
    public static void SetTraceInputOutput(Activity activity, string? input, string? output)
    {
        if (input is not null)
        {
            activity.SetTag("langfuse.trace.input", Cut(input));
        }

        if (output is not null)
        {
            activity.SetTag("langfuse.trace.output", Cut(output));
        }
    }

    private static void Set(Activity activity, string attribute, string langfuseAttribute, string? text)
    {
        if (text is null)
        {
            return;
        }

        var value = Cut(text);
        activity.SetTag(attribute, value);
        activity.SetTag(langfuseAttribute, value);
    }

    private static string Cut(string text) => text.Length <= MaxLength ? text : string.Concat(text.AsSpan(0, MaxLength - 1), "…");
}
