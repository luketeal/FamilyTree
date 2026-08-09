using System.Text.Json.Serialization;

namespace WasmProbe;

// A C# port of family-graph.js, kept deterministic and bit-compatible with it.
//
// It is a port rather than an approximation so that the probe's layout and
// render timings can be compared directly against the browser harness's. If
// the two built different graphs, any difference between the numbers would be
// unattributable — the whole point of the probe is to isolate the interop
// boundary as the only thing that changed.
//
// The DTO shape deliberately mirrors what the plan specifies for TreeGraphDto
// in PR 10: flat records, nodes carrying IsPhantom, edges typed and carrying
// certainty. Measuring a smaller payload than the real one would flatter the
// interop cost.

public sealed record GraphNodeDto(
    string Id,
    int Generation,
    bool IsPhantom,
    string? Name,
    int? BirthYear,
    int? DeathYear);

public sealed record GraphEdgeDto(
    string Id,
    string Source,
    string Target,
    string Kind,
    string Certainty);

public sealed record SyntheticGraphDto(
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SyntheticGraphDto))]
public partial class ProbeJsonContext : JsonSerializerContext;

public static class SyntheticGraph
{
    private const string Biological = "biological";
    private const string Adoptive = "adoptive";
    private const string Marriage = "marriage";
    private const string MarriageEnded = "marriage-ended";
    private const string Stepparent = "stepparent";

    private static readonly string[] Surnames =
    [
        "Ashcombe", "Bellweather", "Carrow", "Danforth", "Elmsley", "Fairbrook",
        "Grantham", "Halloway", "Ingerson", "Jessop", "Kirkbride", "Lowsley",
        "Marchetti", "Norwood", "Oakhurst", "Pemberton", "Quillon", "Rothery",
    ];

    private static readonly string[] Given =
    [
        "Adeline", "Bartholomew", "Clementine", "Desmond", "Eleanor", "Ferdinand",
        "Georgiana", "Horatio", "Isolde", "Jasper", "Katharine", "Leopold",
        "Marguerite", "Nathaniel", "Ottoline", "Percival", "Rosalind", "Sebastian",
        "Theodora", "Ulric", "Vivienne", "Wilhelmina", "Xavier", "Yolande",
    ];

    /// <summary>Mulberry32, matching the JavaScript generator bit for bit.</summary>
    private sealed class Mulberry32(uint seed)
    {
        private uint _a = seed;

        public double Next()
        {
            _a += 0x6d2b79f5u;
            var t = _a;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    public static SyntheticGraphDto Build(int size = 500, uint seed = 20260809)
    {
        var rand = new Mulberry32(seed);
        string Pick(string[] xs) => xs[(int)Math.Floor(rand.Next() * xs.Length)];
        bool Chance(double p) => rand.Next() < p;

        var nodes = new List<GraphNodeDto>();
        var edges = new List<GraphEdgeDto>();
        var n = 0;

        GraphNodeDto Person(int generation, string surname, bool phantom = false)
        {
            var id = $"p{n++}";
            var birthYear = 1880 + generation * 28 + (int)Math.Floor(rand.Next() * 8);
            var deathYear = birthYear + 62 + (int)Math.Floor(rand.Next() * 28);
            var node = new GraphNodeDto(
                id,
                generation,
                phantom,
                phantom ? null : $"{Pick(Given)} {surname}",
                phantom ? null : birthYear,
                phantom ? null : deathYear < 2026 ? deathYear : null);
            nodes.Add(node);
            return node;
        }

        void Edge(string source, string target, string kind) =>
            edges.Add(new GraphEdgeDto($"e{edges.Count}", source, target, kind, "Confirmed"));

        const int Generations = 6;
        var adoptions = 0;
        var frontier = new List<(GraphNodeDto[] Parents, string Surname)>();
        var foundingCouples = Math.Max(2, (int)Math.Round(size / 160.0, MidpointRounding.AwayFromZero));

        for (var i = 0; i < foundingCouples; i++)
        {
            var surname = Surnames[i % Surnames.Length];
            var a = Person(0, surname);
            var b = Person(0, Surnames[(i + 7) % Surnames.Length]);
            Edge(a.Id, b.Id, Marriage);
            frontier.Add(([a, b], surname));
        }

        for (var g = 0; g < Generations && nodes.Count < size; g++)
        {
            var next = new List<(GraphNodeDto[] Parents, string Surname)>();

            foreach (var couple in frontier)
            {
                if (nodes.Count >= size)
                {
                    break;
                }

                var childCount = 1 + (int)Math.Floor(rand.Next() * 3);
                var children = new List<GraphNodeDto>();

                for (var c = 0; c < childCount; c++)
                {
                    var child = Person(g + 1, couple.Surname);
                    foreach (var parent in couple.Parents)
                    {
                        Edge(parent.Id, child.Id, Biological);
                    }
                    children.Add(child);
                }

                // Mirrors the forced first adoption in family-graph.js. Both
                // short-circuit, so when the adoption is forced neither draws
                // from the generator — the two sequences stay in step.
                var forceAdoption = adoptions == 0 && g == 0;
                if ((forceAdoption || Chance(0.1)) && children.Count > 0)
                {
                    var adopter = Person(g, Pick(Surnames));
                    Edge(adopter.Id, children[0].Id, Adoptive);
                    adoptions++;
                }

                foreach (var child in children)
                {
                    if (nodes.Count >= size)
                    {
                        break;
                    }
                    if (!Chance(0.72))
                    {
                        continue;
                    }

                    var spouse = Person(g + 1, Pick(Surnames));
                    var remarried = Chance(0.18);
                    Edge(child.Id, spouse.Id, remarried ? MarriageEnded : Marriage);
                    next.Add(([child, spouse], couple.Surname));

                    if (remarried && nodes.Count < size)
                    {
                        var secondSpouse = Person(g + 1, Pick(Surnames));
                        Edge(child.Id, secondSpouse.Id, Marriage);
                        next.Add(([child, secondSpouse], couple.Surname));
                        Edge(secondSpouse.Id, spouse.Id, Stepparent);
                    }
                }
            }

            if (next.Count > 0 && nodes.Count < size)
            {
                var orphan = Person(g + 1, Pick(Surnames));
                var phantom = Person(g, string.Empty, phantom: true);
                Edge(phantom.Id, orphan.Id, Biological);
                var partner = Person(g + 1, Pick(Surnames));
                next.Add(([orphan, partner], Pick(Surnames)));
                Edge(orphan.Id, partner.Id, Marriage);
            }

            frontier = next;
        }

        return new SyntheticGraphDto(nodes, edges);
    }
}
