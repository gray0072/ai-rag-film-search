using System.Diagnostics;
using System.Globalization;
using System.Text;
using FilmSearch.AI.Providers;
using Microsoft.Extensions.AI;

namespace FilmSearch.AiPlayground.Benchmark;

/// <summary>
/// Small embedding model benchmark (I05). The catalog texts are English, users write in English, Russian and Swedish,
/// so the main question is cross-lingual: does a Russian or Swedish query land next to the right English overview?
/// <list type="bullet">
/// <item>Retrieval: 10 English overviews (document prefix) × 10 queries per language (query prefix): top-1 and MRR.</item>
/// <item>Translation pairs: the same query in en/ru/sv; mean cosine of translations vs. of unrelated queries.</item>
/// </list>
/// </summary>
internal sealed class EmbeddingBenchmark(IEmbeddingGenerator<string, Embedding<float>> generator, EmbeddingProfileOptions profile)
{
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"## Embedding model `{profile.Model}`").AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Query prefix `{profile.QueryPrefix}`, document prefix `{profile.DocumentPrefix}`.").AppendLine();

        var stopwatch = Stopwatch.StartNew();
        var documents = await EmbedAsync(Movies.Select(m => profile.FormatDocument(m.Overview)), ct);
        var documentsMs = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        _ = await EmbedAsync([profile.FormatQuery(Movies[0].Queries["en"])], ct);
        var singleQueryMs = stopwatch.Elapsed.TotalMilliseconds;

        var queries = new Dictionary<string, float[][]>();
        foreach (var lang in Languages)
        {
            queries[lang] = await EmbedAsync(Movies.Select(m => profile.FormatQuery(m.Queries[lang])), ct);
        }

        var dimensions = documents[0].Length;
        report.AppendLine("| Metric | en | ru | sv |").AppendLine("|---|---|---|---|");

        var retrieval = Languages.ToDictionary(lang => lang, lang => Retrieval(queries[lang], documents));
        report.AppendLine(CultureInfo.InvariantCulture,
            $"| Retrieval top-1 (of {Movies.Count}) | {retrieval["en"].Top1} | {retrieval["ru"].Top1} | {retrieval["sv"].Top1} |");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"| Retrieval MRR | {retrieval["en"].Mrr:F2} | {retrieval["ru"].Mrr:F2} | {retrieval["sv"].Mrr:F2} |");

        var ru = Pairs(queries["en"], queries["ru"]);
        var sv = Pairs(queries["en"], queries["sv"]);
        report.AppendLine(CultureInfo.InvariantCulture,
            $"| Translation pair found (top-1 of {Movies.Count}) | – | {ru.Top1} | {sv.Top1} |");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"| Cosine translation / unrelated | – | {ru.Matched:F2} / {ru.Unrelated:F2} | {sv.Matched:F2} / {sv.Unrelated:F2} |").AppendLine();

        report.AppendLine(CultureInfo.InvariantCulture, $"Dimensions: {dimensions} (profile: {profile.Dimensions}). "
            + $"Latency: {Movies.Count} documents in one batch {documentsMs:F0} ms, one query {singleQueryMs:F0} ms.").AppendLine();

        report.AppendLine("Misses (query → top document):").AppendLine();
        foreach (var lang in Languages)
        {
            foreach (var (index, top) in retrieval[lang].Misses)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {lang}: \"{Movies[index].Queries[lang]}\" → {Movies[top].Title} (expected {Movies[index].Title})");
            }
        }

        return report.ToString();
    }

    private async Task<float[][]> EmbedAsync(IEnumerable<string> texts, CancellationToken ct)
    {
        var result = await generator.GenerateAsync(texts, cancellationToken: ct);
        return result.Select(e => e.Vector.ToArray()).ToArray();
    }

    private static (int Top1, double Mrr, List<(int Index, int Top)> Misses) Retrieval(float[][] queries, float[][] documents)
    {
        int top1 = 0;
        double reciprocalRanks = 0;
        var misses = new List<(int, int)>();

        for (var i = 0; i < queries.Length; i++)
        {
            var ranking = documents
                .Select((d, index) => (Index: index, Score: Similarity.Cosine(queries[i], d)))
                .OrderByDescending(x => x.Score)
                .Select(x => x.Index)
                .ToList();
            var rank = ranking.IndexOf(i) + 1;
            reciprocalRanks += 1.0 / rank;
            if (rank == 1)
            {
                top1++;
            }
            else
            {
                misses.Add((i, ranking[0]));
            }
        }

        return (top1, reciprocalRanks / queries.Length, misses);
    }

    private static (int Top1, double Matched, double Unrelated) Pairs(float[][] english, float[][] other)
    {
        int top1 = 0;
        double matched = 0, unrelated = 0;
        for (var i = 0; i < english.Length; i++)
        {
            var scores = other.Select(o => Similarity.Cosine(english[i], o)).ToArray();
            matched += scores[i];
            unrelated += (scores.Sum() - scores[i]) / (scores.Length - 1);
            top1 += Array.IndexOf(scores, scores.Max()) == i ? 1 : 0;
        }

        return (top1, matched / english.Length, unrelated / english.Length);
    }

    private static readonly string[] Languages = ["en", "ru", "sv"];

    private sealed record TestMovie(string Title, string Overview, Dictionary<string, string> Queries);

    private static TestMovie Movie(string title, string overview, string en, string ru, string sv) =>
        new(title, overview, new() { ["en"] = en, ["ru"] = ru, ["sv"] = sv });

    // Overviews are short paraphrases written for this benchmark. Queries describe the plot without the title.
    private static readonly IReadOnlyList<TestMovie> Movies =
    [
        Movie("The Godfather",
            "The aging patriarch of an organized crime dynasty transfers control of his clandestine empire to his reluctant youngest son.",
            "a saga about a mafia family", "фильм про мафиозную семью", "en film om en maffiafamilj"),
        Movie("Inception",
            "A thief who steals corporate secrets through dream-sharing technology is given the task of planting an idea into the mind of a CEO.",
            "a heist inside dreams", "ограбление во сне", "en kupp inne i drömmar"),
        Movie("Titanic",
            "A young aristocrat falls in love with a poor artist aboard a luxurious ship that sinks on its maiden voyage.",
            "a love story on a sinking ship", "история любви на тонущем корабле", "en kärlekshistoria på ett sjunkande skepp"),
        Movie("Toy Story",
            "A cowboy doll feels threatened when a new spaceman action figure becomes the favorite toy of a boy.",
            "toys that come alive", "игрушки, которые оживают", "leksaker som blir levande"),
        Movie("Alien",
            "The crew of a commercial spacecraft is hunted by a deadly extraterrestrial creature after answering a distress call.",
            "a monster hunts the crew of a spaceship", "монстр охотится на экипаж космического корабля", "ett monster jagar besättningen på ett rymdskepp"),
        Movie("The Shawshank Redemption",
            "A banker sentenced to life in prison for murder befriends a fellow inmate and keeps hope alive over two decades.",
            "prison escape, friendship and hope", "побег из тюрьмы, дружба и надежда", "flykt från fängelse, vänskap och hopp"),
        Movie("Finding Nemo",
            "A timid clownfish crosses the ocean to find his son, who was caught by a diver and put in a dentist's aquarium.",
            "a father fish searching for his son", "рыбка-отец ищет своего сына", "en fiskpappa letar efter sin son"),
        Movie("The Matrix",
            "A hacker learns that the world he lives in is a simulation and joins a rebellion against the machines that control it.",
            "reality is a computer simulation", "реальность оказалась компьютерной симуляцией", "verkligheten är en datorsimulering"),
        Movie("Jurassic Park",
            "Cloned dinosaurs break out in an island theme park and hunt the visitors during a storm.",
            "dinosaurs in an amusement park", "динозавры в парке развлечений", "dinosaurier i en nöjespark"),
        Movie("The Devil Wears Prada",
            "A young journalist takes a job as an assistant to the demanding editor of a fashion magazine.",
            "working for a cruel boss at a fashion magazine", "работа на жестокую начальницу в модном журнале", "jobba för en elak chef på en modetidning"),
    ];
}
