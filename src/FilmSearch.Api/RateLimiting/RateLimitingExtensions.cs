using System.Globalization;
using System.Threading.RateLimiting;
using FilmSearch.Api.Owners;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FilmSearch.Api.RateLimiting;

/// <summary>Names of the rate limiting policies (spec 10 §4, I19).</summary>
public static class RateLimitPolicies
{
    /// <summary>Chat messages per owner: every message starts paid or slow model calls.</summary>
    public const string Chat = "chat";

    /// <summary>New conversations per client IP: a script that throws its owner cookie away gets a new owner, and with it a new chat limit and token budget, but not a new IP.</summary>
    public const string NewConversation = "chat-new";

    /// <summary>Search requests per client IP: semantic search embeds the query and <c>understand</c> calls the chat model.</summary>
    public const string Search = "search";

    /// <summary>The <c>code</c> of the 429 Problem Details.</summary>
    public const string RejectedCode = "rateLimited";
}

/// <summary>The <c>RateLimiting</c> configuration section.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Off: no request is limited (integration tests that send many requests from one client).</summary>
    public bool Enabled { get; set; } = true;

    public RateLimitOptions Chat { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 };

    public RateLimitOptions NewConversation { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RateLimitOptions Search { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };
}

/// <summary>A token bucket: <see cref="PermitLimit"/> requests at once, then one more every <see cref="WindowSeconds"/> / <see cref="PermitLimit"/> seconds (on average <see cref="PermitLimit"/> per window).</summary>
public sealed class RateLimitOptions
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}

public static class RateLimitingExtensions
{
    /// <summary>
    /// Rate limiting policies (spec 10 §4, NFR-7): chat messages per owner, new conversations and searches per client IP.
    /// Token buckets without a queue: a burst up to the limit is fine, the steady rate is the limit per window, and a
    /// request over it gets 429 Problem Details at once, with <c>code: "rateLimited"</c> and a <c>Retry-After</c> header
    /// (the time until the next token; a sliding window cannot tell it). ASP.NET Core counts the decisions in the
    /// <c>aspnetcore.rate_limiting.*</c> metrics (by policy and result). The client IP is the connection's address:
    /// behind a reverse proxy the forwarded headers must be applied first (I20).
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(
                o => new[] { o.Chat, o.NewConversation, o.Search }.All(p => p.PermitLimit is >= 1 and <= 100_000 && p.WindowSeconds is >= 1 and <= 86_400),
                "RateLimiting: every policy needs PermitLimit 1–100000 and WindowSeconds 1–86400.")
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;
            limiter.AddPolicy(RateLimitPolicies.Chat, context => Partition(context, o => o.Chat, OwnerKey(context)));
            limiter.AddPolicy(RateLimitPolicies.NewConversation, context => Partition(context, o => o.NewConversation, IpKey(context)));
            limiter.AddPolicy(RateLimitPolicies.Search, context => Partition(context, o => o.Search, IpKey(context)));
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext context, Func<RateLimitingOptions, RateLimitOptions> policy, string key)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        if (!options.Enabled)
        {
            return RateLimitPartition.GetNoLimiter("off");
        }

        var settings = policy(options);
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = settings.PermitLimit,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromSeconds((double)settings.WindowSeconds / settings.PermitLimit),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    /// <summary>The owner (user or anonymous session); the client IP when the owner is not resolved.</summary>
    private static string OwnerKey(HttpContext context) =>
        context.RequestServices.GetRequiredService<OwnerContext>() is { IsResolved: true } owner
            ? $"owner:{owner.OwnerId}"
            : IpKey(context);

    private static string IpKey(HttpContext context) => $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var http = rejected.HttpContext;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests. Please wait a moment and try again.",
            Extensions = { ["code"] = RateLimitPolicies.RejectedCode },
        };

        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            problem.Extensions["retryAfterSeconds"] = seconds;
        }

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
        });
    }
}
