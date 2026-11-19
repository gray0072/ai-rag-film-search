using System.Runtime.CompilerServices;
using FilmSearch.AI.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FilmSearch.AI.Providers;

/// <summary>
/// The unkeyed <see cref="IChatClient"/> when <c>AI:FallbackChatProfile</c> is set (I19): calls go to the active profile;
/// when it fails with a provider failure (<see cref="AiProviderErrors.IsProviderFailure"/>: down, timeout, open circuit,
/// HTTP error) before anything was streamed, the same call goes to the fallback profile. A stream that fails after its
/// first update is not switched: the user has already seen part of the answer, so the error goes to the caller.
/// <para>
/// Every response and update of the fallback is marked with <see cref="FallbackProfileKey"/> in
/// <c>AdditionalProperties</c>, so the assistant can flag the answer and price its tokens with the fallback's model.
/// Counter <c>ai.fallbacks</c> (from, to, operation).
/// </para>
/// </summary>
internal sealed partial class FallbackChatClient(
    IChatClient primary,
    string primaryProfile,
    IChatClient fallback,
    string fallbackProfile,
    AiMetrics metrics,
    ILogger<FallbackChatClient> logger) : IChatClient
{
    /// <summary>Key in <c>AdditionalProperties</c> of responses and updates written by the fallback profile; the value is its name.</summary>
    public const string FallbackProfileKey = "filmsearch.fallback_profile";

    /// <summary>The fallback profile that wrote this response, or null.</summary>
    public static string? FallbackProfileOf(AdditionalPropertiesDictionary? properties) =>
        properties?.TryGetValue(FallbackProfileKey, out var value) == true ? value as string : null;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? [.. messages];
        try
        {
            return await primary.GetResponseAsync(list, options, cancellationToken);
        }
        catch (Exception ex) when (AiProviderErrors.IsProviderFailure(ex, cancellationToken))
        {
            OnFallback("chat", ex);
        }

        var response = await fallback.GetResponseAsync(list, options, cancellationToken);
        (response.AdditionalProperties ??= [])[FallbackProfileKey] = fallbackProfile;
        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? [.. messages];

        // The first update decides: a yield cannot stand in a try block with a catch, so the enumerator is moved by hand.
        var stream = primary.GetStreamingResponseAsync(list, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        var failedBeforeFirstUpdate = false;
        try
        {
            bool hasFirst;
            try
            {
                hasFirst = await stream.MoveNextAsync();
            }
            catch (Exception ex) when (AiProviderErrors.IsProviderFailure(ex, cancellationToken))
            {
                OnFallback("chat_stream", ex);
                failedBeforeFirstUpdate = true;
                hasFirst = false;
            }

            if (hasFirst)
            {
                yield return stream.Current;
                while (await stream.MoveNextAsync())
                {
                    yield return stream.Current;
                }
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }

        if (!failedBeforeFirstUpdate)
        {
            yield break;
        }

        await foreach (var update in fallback.GetStreamingResponseAsync(list, options, cancellationToken))
        {
            (update.AdditionalProperties ??= [])[FallbackProfileKey] = fallbackProfile;
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(FallbackChatClient) ? this : primary.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        // The profile clients are singletons of the container; it disposes them.
    }

    private void OnFallback(string operation, Exception exception)
    {
        metrics.RecordFallback(primaryProfile, fallbackProfile, operation);
        LogFallback(logger, primaryProfile, fallbackProfile, exception.Message, exception);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Chat profile {Primary} failed ({Error}); the call goes to the fallback profile {Fallback}")]
    private static partial void LogFallback(ILogger logger, string primary, string fallback, string error, Exception exception);
}
