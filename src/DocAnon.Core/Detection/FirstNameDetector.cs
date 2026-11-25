using System.Text.RegularExpressions;
using DocAnon.Core.Model;
using DocAnon.Core.Text;

namespace DocAnon.Core.Detection;

/// <summary>
/// Finds common first names, alone or followed by surnames and initials ("RENATO T.", "Otavio B
/// Moreira"). It complements NER in tables and title blocks, where names appear without the
/// sentence context that the model needs.
/// </summary>
public sealed partial class FirstNameDetector : IDetector
{
    public string Id => "nomes";

    private static readonly HashSet<string> Names = new(new[]
    {
        // Brazilian first names (masculine).
        "adriano", "afonso", "alberto", "alessandro", "alexandre", "alex", "alfredo", "alisson", "alvaro", "anderson", "andre", "angelo", "antonio", "armando", "arnaldo", "arthur", "artur", "augusto",
        "benedito", "bernardo", "breno", "bruno", "caio", "carlos", "cassio", "cesar", "claudio", "cleber", "cristiano", "daniel", "danilo", "davi", "david", "denis", "diego", "diogo", "douglas",
        "edson", "eduardo", "elias", "emerson", "enzo", "erick", "erik", "ernesto", "esdras", "eugenio", "evandro", "everton", "fabiano", "fabio", "fabricio", "felipe", "fernando", "flavio", "francisco",
        "gabriel", "geraldo", "gilberto", "gilmar", "giovanni", "guilherme", "gustavo", "heitor", "helio", "henrique", "hercules", "hugo", "igor", "ivan", "jair", "jefferson", "joao", "joaquim", "jonas",
        "jorge", "jose", "julio", "juliano", "kleber", "leandro", "leonardo", "lucas", "luciano", "luis", "luiz", "manoel", "manuel", "marcelo", "marcio", "marco", "marcos", "mario", "mateus", "matheus",
        "mauricio", "mauro", "max", "michel", "miguel", "milton", "moises", "murilo", "nelson", "nilton", "osvaldo", "oswaldo", "otavio", "pablo", "paulo", "pedro", "rafael", "raimundo", "reinaldo", "renan",
        "renato", "ricardo", "roberto", "rodrigo", "rogerio", "romulo", "ronaldo", "rubens", "samuel", "sandro", "sebastiao", "sergio", "silvio", "tadeu", "thiago", "tiago", "ulisses", "valdir", "valter",
        "vicente", "victor", "vinicius", "vitor", "wagner", "waldir", "walter", "washington", "wellington", "william", "wilson",
        // Brazilian first names (feminine).
        "adriana", "alessandra", "alice", "aline", "amanda", "ana", "andrea", "andreia", "angela", "beatriz", "bianca", "bruna", "camila", "carla", "carolina", "caroline", "catarina", "cecilia", "cintia",
        "claudia", "cristiane", "cristina", "daniela", "debora", "denise", "eliane", "elisa", "elaine", "fabiana", "fatima", "fernanda", "flavia", "francisca", "gabriela", "giovana", "helena", "heloisa",
        "isabel", "isabela", "jaqueline", "jessica", "joana", "juliana", "julia", "karina", "katia", "larissa", "laura", "leticia", "lidia", "lilian", "livia", "lorena", "luana", "lucia", "luciana",
        "luiza", "marcela", "marcia", "margarete", "maria", "mariana", "marina", "marisa", "marta", "monica", "natalia", "patricia", "paula", "priscila", "raquel", "rebeca", "regina", "renata", "rita",
        "roberta", "sabrina", "sandra", "silvia", "simone", "sonia", "suzana", "tania", "tatiana", "teresa", "tereza", "vanessa", "vera", "veronica", "viviane",
        // English first names.
        "james", "john", "robert", "michael", "william", "richard", "joseph", "thomas", "charles", "christopher", "matthew", "anthony", "mark", "steven", "paul", "andrew", "joshua", "kenneth", "kevin",
        "brian", "george", "timothy", "ronald", "jason", "jeffrey", "ryan", "jacob", "gary", "nicholas", "eric", "jonathan", "stephen", "larry", "justin", "scott", "brandon", "benjamin", "gregory",
        "mary", "patricia", "jennifer", "linda", "elizabeth", "barbara", "susan", "jessica", "sarah", "karen", "nancy", "lisa", "betty", "margaret", "sandra", "ashley", "kimberly", "emily", "donna",
        "michelle", "carol", "amanda", "melissa", "deborah", "stephanie", "rebecca", "laura", "sharon", "cynthia", "kathleen", "amy", "shirley", "angela", "helen", "anna", "brenda", "pamela", "nicole",
        "tony", "peter", "frank", "raymond", "jack", "dennis", "jerry", "tyler", "aaron", "henry", "douglas", "peter", "adam", "nathan", "zachary", "kyle", "walter", "harold", "jeremy", "ethan", "carl",
    }, StringComparer.Ordinal);

    [GeneratedRegex(@"(?<![\p{L}\d])(?<first>\p{L}{3,})(?<rest>(?:\s+(?:(?:de|da|do|dos|das|e)\s+)?(?:\p{Lu}\.?(?![\p{L}])|\p{Lu}[\p{L}'’]+)){0,4})")]
    private static partial Regex CandidateRx();

    [GeneratedRegex(@"(?:s[aã]o|santo|santa|s\.)\s+$", RegexOptions.IgnoreCase)]
    private static partial Regex SaintBeforeRx();

    public IEnumerable<DetectedSpan> Detect(DetectionContext context)
    {
        string text = context.Text;
        foreach (Match m in CandidateRx().Matches(text))
        {
            var first = m.Groups["first"];
            if (!char.IsUpper(first.Value[0])) continue;
            if (!Names.Contains(TextFold.Key(first.Value))) continue;
            // "São Paulo" and "Santa Maria" are places.
            if (SaintBeforeRx().IsMatch(text.AsSpan(Math.Max(0, first.Index - 8), first.Index - Math.Max(0, first.Index - 8)).ToString())) continue;
            string rest = m.Groups["rest"].Value;
            // Only words that look like names continue it; a capitalized common word ends it.
            int length = first.Length;
            if (rest.Length > 0)
            {
                // Words in the same case style as the first name continue it ("RENATO T.", "Otavio B Moreira").
                bool allCaps = first.Value.All(c => !char.IsLetter(c) || char.IsUpper(c));
                int consumed = 0;
                foreach (Match w in Regex.Matches(rest, @"\s+(?:(?:de|da|do|dos|das|e)\s+)?(\p{Lu}\.?(?![\p{L}])|\p{Lu}[\p{L}'’]+)"))
                {
                    if (w.Index != consumed) break;
                    string word = w.Groups[1].Value;
                    bool wordCaps = word.All(c => !char.IsLetter(c) || char.IsUpper(c));
                    if (word.Length > 2 && wordCaps != allCaps) break;
                    // A line break, a wide gap or a following colon ("VERIF.:") ends the name.
                    string gap = rest.Substring(w.Index, w.Groups[1].Index - w.Index);
                    if (gap.Contains('\n') || gap.Contains("  ")) break;
                    int after = w.Index + w.Length;
                    if (after < rest.Length && rest[after] == ':' || after + 1 < rest.Length && rest[after] == '.' && rest[after + 1] == ':') break;
                    consumed = w.Index + w.Length;
                }
                length += consumed;
            }
            string surface = text.Substring(first.Index, length);
            bool single = length == first.Length;
            yield return new DetectedSpan(first.Index, length, EntityType.PESSOA, surface, Id, single ? 0.65 : 0.8)
            {
                Note = single ? "prenome comum" : "prenome comum seguido de sobrenome ou inicial",
            };
        }
    }
}
