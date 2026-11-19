using System.Diagnostics.Metrics;
using FilmSearch.AI.Telemetry;
using Microsoft.Extensions.Caching.Memory;

namespace FilmSearch.AI.Caching;

/// <summary>
/// A bounded in-memory cache of AI results (I19): its own <see cref="MemoryCache"/> with an entry limit, so a flood of
/// unique queries cannot grow the process without bound (when full, new entries are dropped until expired ones are
/// compacted). Every lookup is counted in <c>cache.lookups</c> (cache, result = hit / miss); the hit rate is
/// hit / (hit + miss). <c>cache.entries</c> is the current size. A cache with 0 minutes or 0 entries is off and not counted.
/// </summary>
internal abstract class LookupCache<TValue> : ICacheSize, IDisposable
    where TValue : class
{
    private readonly MemoryCache? _cache;
    private readonly TimeSpan _lifetime;
    private readonly CacheMetrics _metrics;

    protected LookupCache(string name, int maxEntries, TimeSpan lifetime, CacheMetrics metrics)
    {
        Name = name;
        _lifetime = lifetime;
        _metrics = metrics;
        if (maxEntries > 0 && lifetime > TimeSpan.Zero)
        {
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxEntries });
            metrics.Register(this);
        }
    }

    /// <summary>The <c>cache</c> tag of the metrics.</summary>
    public string Name { get; }

    public bool Enabled => _cache is not null;

    public long Count => _cache?.Count ?? 0;

    public bool TryGet(string key, out TValue? value)
    {
        value = null;
        if (_cache is null)
        {
            return false;
        }

        var hit = _cache.TryGetValue(key, out value) && value is not null;
        _metrics.RecordLookup(Name, hit);
        return hit;
    }

    public void Set(string key, TValue value) =>
        _cache?.Set(key, value, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = _lifetime });

    public void Dispose()
    {
        _metrics.Unregister(this);
        _cache?.Dispose();
    }
}

/// <summary>What <c>cache.entries</c> reads.</summary>
internal interface ICacheSize
{
    string Name { get; }

    long Count { get; }
}

/// <summary>Cache metrics (meter <c>FilmSearch</c>, spec 10 §3).</summary>
public sealed class CacheMetrics
{
    private readonly Counter<long> _lookups;
    private readonly List<ICacheSize> _caches = [];

    public CacheMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(AiMetrics.MeterName);
        _lookups = meter.CreateCounter<long>(
            "cache.lookups", unit: "{lookup}", description: "Lookups in the AI result caches (query embeddings, query understanding) by result: hit or miss.");
        meter.CreateObservableGauge("cache.entries", ObserveEntries, unit: "{entry}", description: "Entries in the AI result caches.");
    }

    internal void RecordLookup(string cache, bool hit) =>
        _lookups.Add(1, new("cache", cache), new("result", hit ? "hit" : "miss"));

    internal void Register(ICacheSize cache)
    {
        lock (_caches)
        {
            _caches.Add(cache);
        }
    }

    internal void Unregister(ICacheSize cache)
    {
        lock (_caches)
        {
            _caches.Remove(cache);
        }
    }

    private List<Measurement<long>> ObserveEntries()
    {
        lock (_caches)
        {
            return [.. _caches.Select(c => new Measurement<long>(c.Count, new KeyValuePair<string, object?>("cache", c.Name)))];
        }
    }
}
