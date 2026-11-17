namespace FilmSearch.Infrastructure.Search.Semantic;

/// <summary>Vector arithmetic for query vectors (I07: "movies like X").</summary>
internal static class QueryVectors
{
    /// <summary>The vector scaled to length 1. A zero vector is returned unchanged.</summary>
    public static float[] Normalize(ReadOnlySpan<float> vector)
    {
        var length = 0.0;
        foreach (var value in vector)
        {
            length += (double)value * value;
        }

        length = Math.Sqrt(length);
        var result = vector.ToArray();
        if (length > 0)
        {
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = (float)(result[i] / length);
            }
        }

        return result;
    }

    /// <summary>
    /// <c>normalize(normalize(query) + mean(normalize(others)))</c>: the query and the named movies get equal weight,
    /// however many movies there are. Cosine distance ignores length, but averaging does not: every part is normalized
    /// first so a longer vector does not dominate.
    /// </summary>
    public static float[] Blend(ReadOnlyMemory<float> query, IReadOnlyList<ReadOnlyMemory<float>> others)
    {
        if (others.Count == 0)
        {
            return query.ToArray();
        }

        if (others.Any(o => o.Length != query.Length))
        {
            throw new ArgumentException("All vectors must have the same length.", nameof(others));
        }

        var sum = Normalize(query.Span);
        foreach (var other in others)
        {
            var normalized = Normalize(other.Span);
            for (var i = 0; i < sum.Length; i++)
            {
                sum[i] += normalized[i] / others.Count;
            }
        }

        return Normalize(sum);
    }
}
