using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace FilmSearch.AI.Prompts;

/// <summary>
/// A versioned prompt template (<c>Prompts/&lt;name&gt;.vN.md</c>). <see cref="Id"/> (<c>name.vN</c>) goes to telemetry and is
/// stored with the results it produced, so a change of behaviour can be traced to a prompt version.
/// </summary>
public sealed partial record PromptTemplate(string Name, int Version, string Text)
{
    public string Id => $"{Name}.v{Version.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Replaces <c>{{key}}</c> placeholders.</summary>
    /// <exception cref="InvalidOperationException">A placeholder has no value, or a value has no placeholder.</exception>
    public string Render(IReadOnlyDictionary<string, string> values)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();
        var text = Placeholder().Replace(Text, match =>
        {
            var key = match.Groups[1].Value;
            if (values.TryGetValue(key, out var value))
            {
                used.Add(key);
                return value;
            }

            missing.Add(key);
            return match.Value;
        });

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Prompt {Id} needs values for: {string.Join(", ", missing.Distinct())}.");
        }

        var unused = values.Keys.Where(k => !used.Contains(k)).ToList();
        if (unused.Count > 0)
        {
            throw new InvalidOperationException($"Prompt {Id} has no placeholders for: {string.Join(", ", unused)}.");
        }

        return text;
    }

    [GeneratedRegex(@"\{\{([a-z0-9_]+)\}\}")]
    private static partial Regex Placeholder();
}

/// <summary>
/// The prompt files embedded in this assembly (<c>Prompts/*.vN.md</c>, see the csproj). Prompts are code: they are
/// reviewed, versioned and shipped with the build, not edited at run time.
/// </summary>
public sealed partial class PromptLibrary
{
    private const string ResourcePrefix = "FilmSearch.AI.Prompts.";

    private readonly Dictionary<string, SortedDictionary<int, PromptTemplate>> _prompts = new(StringComparer.Ordinal);

    public PromptLibrary()
        : this(typeof(PromptLibrary).Assembly)
    {
    }

    internal PromptLibrary(Assembly assembly)
    {
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var match = ResourceName().Match(resource);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var template = new PromptTemplate(
                match.Groups["name"].Value,
                int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture),
                reader.ReadToEnd().ReplaceLineEndings("\n"));

            if (!_prompts.TryGetValue(template.Name, out var versions))
            {
                versions = [];
                _prompts[template.Name] = versions;
            }

            versions[template.Version] = template;
        }
    }

    public IReadOnlyCollection<string> Names => _prompts.Keys;

    /// <summary>The given version of a prompt, or its latest version when <paramref name="version"/> is null.</summary>
    /// <exception cref="InvalidOperationException">No such prompt or version.</exception>
    public PromptTemplate Get(string name, int? version = null)
    {
        if (!_prompts.TryGetValue(name, out var versions))
        {
            throw new InvalidOperationException($"Prompt '{name}' is not embedded (src/FilmSearch.AI/Prompts/{name}.vN.md).");
        }

        if (version is null)
        {
            return versions.Values.Last();
        }

        return versions.TryGetValue(version.Value, out var template)
            ? template
            : throw new InvalidOperationException($"Prompt '{name}' has no version {version}. Available: {string.Join(", ", versions.Keys)}.");
    }

    [GeneratedRegex(@"^FilmSearch\.AI\.Prompts\.(?<name>[a-z0-9-]+)\.v(?<version>\d+)\.md$")]
    private static partial Regex ResourceName();
}
