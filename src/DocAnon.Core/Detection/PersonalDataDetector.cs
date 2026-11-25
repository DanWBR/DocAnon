using System.Text.RegularExpressions;
using DocAnon.Core.Model;

namespace DocAnon.Core.Detection;

/// <summary>
/// Regex rules for personal identifiers. A rule can require a context keyword in the 40
/// characters before the match, and can run a check-digit validator. If the validator fails on
/// a value that is formatted like the identifier, the span is still reported with a lower score,
/// because a typo in a CPF does not make it less personal.
/// </summary>
public sealed class PersonalDataDetector : IDetector
{
    public string Id => "regex";

    private sealed record Rule(
        EntityType Type,
        Regex Pattern,
        Func<string, bool>? Validator = null,
        string[]? Context = null,
        bool ContextRequired = false,
        bool KeepWhenInvalid = true,
        string Group = "v");

    private const string L = @"(?<![\p{L}\d])";
    private const string R = @"(?![\p{L}\d])";
    private const string NumR = @"(?![\p{L}\d]|[.,]\d)";
    private const RegexOptions O = RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private const RegexOptions OI = O | RegexOptions.IgnoreCase;

    private static readonly string[] CpfCtx = ["cpf"];
    private static readonly string[] CnpjCtx = ["cnpj"];
    private static readonly string[] PisCtx = ["pis", "pasep", "nit", "nis"];
    private static readonly string[] TituloCtx = ["titulo", "eleitor"];
    private static readonly string[] CnhCtx = ["cnh", "habilitacao", "registro nacional"];
    private static readonly string[] RgCtx = ["rg", "r.g.", "registro geral", "identidade", "carteira de identidade"];
    private static readonly string[] CepCtx = ["cep"];
    private static readonly string[] PassCtx = ["passaporte", "passport"];
    private static readonly string[] PlacaCtx = ["placa", "veiculo", "carro", "caminhao", "license plate"];
    private static readonly string[] PixCtx = ["pix", "chave"];
    private static readonly string[] ContaCtx = ["conta corrente", "conta poupanca", "conta:", "conta n", "conta bancaria", "c/c", "agencia", "ag.", "ag:", "account no", "account number", "acct", "banco"];
    private static readonly string[] NascCtx = ["nascid", "nascimento", "data de nasc", "dob", "date of birth", "born"];
    private static readonly string[] SsnCtx = ["ssn", "social security"];

    private static readonly Rule[] Rules =
    [
        new(EntityType.EMAIL, new Regex(@"(?<v>[\p{L}\d._%+-]+@[\p{L}\d-]+(?:\.[\p{L}\d-]+)+)", O)),

        new(EntityType.CPF, new Regex(L + @"(?<v>\d{3}\.\d{3}\.\d{3}-\d{2})" + NumR, O), Validators.Cpf, CpfCtx),
        new(EntityType.CPF, new Regex(L + @"(?<v>\d{11})" + NumR, O), Validators.Cpf, CpfCtx, KeepWhenInvalid: false),

        new(EntityType.CNPJ, new Regex(L + @"(?<v>[A-Z\d]{2}\.[A-Z\d]{3}\.[A-Z\d]{3}/[A-Z\d]{4}-\d{2})" + NumR, O), Validators.Cnpj, CnpjCtx),
        new(EntityType.CNPJ, new Regex(L + @"(?<v>[A-Z\d]{12}\d{2})" + R, O), Validators.Cnpj, CnpjCtx, KeepWhenInvalid: false),

        new(EntityType.PIS, new Regex(L + @"(?<v>\d{3}\.\d{5}\.\d{2}-\d)" + NumR, O), Validators.Pis, PisCtx),
        new(EntityType.PIS, new Regex(L + @"(?<v>\d{11})" + NumR, O), Validators.Pis, PisCtx, ContextRequired: true, KeepWhenInvalid: false),

        new(EntityType.TITULO_ELEITOR, new Regex(L + @"(?<v>\d{4}\s?\d{4}\s?\d{4})" + NumR, O), Validators.TituloEleitor, TituloCtx, ContextRequired: true),
        new(EntityType.CNH, new Regex(L + @"(?<v>\d{11})" + NumR, O), Validators.Cnh, CnhCtx, ContextRequired: true),
        new(EntityType.RG, new Regex(@"(?<![\p{L}\d\-/.])" + @"(?<v>\d{1,2}\.?\d{3}\.?\d{3}-?[\dxX]?)" + R, O), null, RgCtx, ContextRequired: true),

        new(EntityType.CEP, new Regex(L + @"(?<v>\d{5}-\d{3}|\d{2}\.\d{3}-\d{3})" + NumR, O), null, CepCtx),
        new(EntityType.CEP, new Regex(L + @"(?<v>\d{8})" + NumR, O), null, CepCtx, ContextRequired: true),

        new(EntityType.TELEFONE, new Regex(L + @"(?<v>(?:\+55[\s-]?)?(?:\(\d{2}\)\s?|\d{2}[\s-])9?\d{4}[-\s]\d{4})" + NumR, O)),
        new(EntityType.TELEFONE, new Regex(L + @"(?<v>\+55[\s-]?\d{2}[\s-]?9?\d{4}[-\s]?\d{4})" + NumR, O)),
        new(EntityType.TELEFONE, new Regex(L + @"(?<v>(?:\+1[\s-]?)?\(\d{3}\)\s?\d{3}-\d{4}|\+1[\s-]\d{3}[\s-]\d{3}[\s-]\d{4})" + NumR, O)),

        new(EntityType.CARTAO, new Regex(@"(?<![\p{L}\d]|\d[ -])(?<v>\d{4}(?:[ -]?\d{4}){2}[ -]?\d{1,7})(?![\p{L}\d]|[.,]\d|[ -]\d)", O), Validators.CreditCard, KeepWhenInvalid: false),

        new(EntityType.PROCESSO, new Regex(L + @"(?<v>\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4})" + NumR, O), Validators.ProcessoCnj),

        new(EntityType.OAB, new Regex(@"(?<v>OAB[\s/-]*[A-Z]{2}[\s/-]*(?:n[º°o.]\s*)?\d{3,6}|OAB\s*(?:n[º°o.]\s*)?\d{3,6}(?:\s*/\s*[A-Z]{2})?)" + R, O)),
        new(EntityType.CRM, new Regex(@"(?<v>CRM[\s/-]*[A-Z]{2}[\s/-]*(?:n[º°o.]\s*)?\d{4,7}|CRM\s*(?:n[º°o.]\s*)?\d{4,7}(?:\s*/\s*[A-Z]{2})?)" + R, O)),

        new(EntityType.IP, new Regex(L + @"(?<v>(?:\d{1,3}\.){3}\d{1,3})" + NumR, O), Validators.IPv4, KeepWhenInvalid: false),
        new(EntityType.IBAN, new Regex(L + @"(?<v>[A-Z]{2}\d{2}(?:\s?[A-Z\d]{4}){2,7}(?:\s?[A-Z\d]{1,4})?)" + R, O), Validators.Iban, KeepWhenInvalid: false),
        new(EntityType.SSN, new Regex(L + @"(?<v>\d{3}-\d{2}-\d{4})" + NumR, O), Validators.Ssn, SsnCtx, KeepWhenInvalid: false),

        new(EntityType.PASSAPORTE, new Regex(L + @"(?<v>[A-Z]{1,2}\d{6,8})" + R, O), null, PassCtx, ContextRequired: true),
        new(EntityType.PLACA, new Regex(L + @"(?<v>[A-Z]{3}\d[A-Z]\d{2})" + R, O)),
        new(EntityType.PLACA, new Regex(L + @"(?<v>[A-Z]{3}-?\d{4})" + R, O), null, PlacaCtx, ContextRequired: true),
        new(EntityType.PIX, new Regex(L + @"(?<v>[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12})" + R, OI), null, PixCtx, ContextRequired: true),
        new(EntityType.CONTA_BANCARIA, new Regex(@"(?<![\p{L}\d\-/.])(?<v>\d{1,12}-[\dxX]|\d{4,12})" + R, O), null, ContaCtx, ContextRequired: true),
        new(EntityType.DATA_NASCIMENTO, new Regex(
            L + @"(?<v>\d{1,2}[/.-]\d{1,2}[/.-](?:\d{4}|\d{2})|\d{1,2}\s+de\s+\p{L}+\s+de\s+\d{4}|\p{L}+\s+\d{1,2},\s+\d{4}|\d{4}-\d{2}-\d{2})" + R, OI),
            null, NascCtx, ContextRequired: true),
    ];

    public IEnumerable<DetectedSpan> Detect(DetectionContext context)
    {
        foreach (var rule in Rules)
        {
            foreach (Match m in rule.Pattern.Matches(context.Text))
            {
                var g = m.Groups[rule.Group];
                if (!g.Success || g.Length == 0) continue;

                bool hasContext = rule.Context is not null && context.HasContextBefore(g.Index, rule.Context);
                if (rule.ContextRequired && !hasContext) continue;

                bool validated = false;
                double score = 0.85;
                if (rule.Validator is not null)
                {
                    validated = rule.Validator(g.Value);
                    if (!validated)
                    {
                        if (!rule.KeepWhenInvalid && !hasContext) continue;
                        score = 0.6;
                    }
                    else score = 0.98;
                }
                if (hasContext) score = Math.Min(1.0, score + 0.1);

                yield return new DetectedSpan(g.Index, g.Length, rule.Type, g.Value, Id, score)
                {
                    Validated = validated,
                    Note = validated ? "dígito verificador ok" : rule.Validator is not null ? "formato válido, dígito verificador não confere" : hasContext ? "contexto" : null,
                };
            }
        }
    }
}
