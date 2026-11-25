using DocAnon.Core.Model;
using DocAnon.Core.Text;

namespace DocAnon.Core.Detection;

/// <summary>
/// Engineering vocabulary that statistical detectors (NER, LLM) tend to mistake for people or
/// companies: equation-of-state and correlation names, standards bodies, software and common
/// acronyms. These are removed from NER and LLM results only; a profile term or a manual mark
/// with the same text still wins.
/// </summary>
public static class TechnicalTerms
{
    private static readonly HashSet<string> Terms = new(new[]
    {
        // Thermodynamic models and correlations named after people.
        "Peng", "Robinson", "Peng-Robinson", "Peng Robinson", "Soave", "Redlich", "Kwong", "Redlich-Kwong", "SRK", "Soave-Redlich-Kwong",
        "Lee", "Kesler", "Lee-Kesler", "Plöcker", "Lee-Kesler-Plöcker", "Chao", "Seader", "Chao-Seader", "Grayson", "Streed", "Grayson-Streed",
        "Wilson", "Margules", "Van Laar", "NRTL", "UNIQUAC", "UNIFAC", "Rachford", "Rice", "Rachford-Rice", "Antoine", "Raoult", "Henry", "Dalton",
        "Darcy", "Weisbach", "Darcy-Weisbach", "Colebrook", "Colebrook-White", "Fanning", "Moody", "Churchill", "Swamee", "Jain", "Haaland",
        "Beggs", "Brill", "Beggs-Brill", "Beggs & Brill", "Hagedorn", "Brown", "Hagedorn-Brown", "Lockhart", "Martinelli", "Lockhart-Martinelli", "Duns", "Ros", "Orkiszewski",
        "Weymouth", "Panhandle", "Spitzglass", "Mueller", "IGT", "AGA", "Fritzsche", "Isentropic", "Joule", "Thomson", "Joule-Thomson", "Kelvin", "Rankine", "Celsius", "Fahrenheit",
        "Reynolds", "Prandtl", "Nusselt", "Mach", "Newton", "Raphson", "Newton-Raphson", "Broyden", "Gauss", "Seidel", "Gauss-Seidel", "Runge", "Kutta", "Runge-Kutta", "Euler",
        "Bernoulli", "Navier", "Stokes", "Navier-Stokes", "Fourier", "Gibbs", "Helmholtz", "Lewis", "Poynting", "Clausius", "Clapeyron", "Riedel", "Rackett", "Costald", "Letsou", "Stiel", "Twu",
        // Standards bodies and documents.
        "API", "ASME", "ISO", "IEC", "ISA", "NFPA", "ANSI", "ASTM", "ABNT", "NBR", "NR", "DIN", "BS", "EN", "IEEE", "TEMA", "GPSA", "AIChE", "OSHA", "CCPS", "DNV", "Petrobras N",
        // Software.
        "DWSIM", "Aspen", "Aspen Plus", "HYSYS", "Aspen HYSYS", "UniSim", "PRO/II", "Petro-SIM", "Flarenet", "Aspen Flare System Analyzer", "PipeSim", "OLGA", "Excel", "Python", "MATLAB", "AutoCAD", "SmartPlant", "Pipe Network",
        // Process and safety acronyms.
        "PSV", "PSVs", "PRV", "PSE", "UPD", "ESD", "SDV", "BDV", "XV", "PCV", "LCV", "FCV", "TCV", "HAZOP", "LOPA", "SIL", "SIS", "PFD", "P&ID", "PI&D", "FEED", "EPC", "NPSH", "BEP", "LMTD",
        "CFD", "MAWP", "PMTA", "PSH", "PSL", "SCADA", "DCS", "PLC", "CCM", "UTE", "UPGN", "ETE", "GLP", "GNL", "LNG", "LPG", "NGL", "GN", "CO2", "H2S", "BTEX", "TEG", "MEG", "MEA", "MDEA", "DEA",
    }.Select(TextFold.Key), StringComparer.Ordinal);

    public static bool IsTechnical(string text) => Terms.Contains(TextFold.Key(text.Trim(' ', '.', ',', ';', ':', '(', ')')));

    /// <summary>
    /// Drops technical terms from statistical detectors and unchecks single all-caps tokens of up
    /// to five letters, which are usually acronyms.
    /// </summary>
    public static IEnumerable<DetectedSpan> Filter(IEnumerable<DetectedSpan> spans)
    {
        foreach (var s in spans)
        {
            if (s.Detector is "ner" or "llm")
            {
                if (IsTechnical(s.Text)) continue;
                // Names such as "Pipe Network" mix technical words; drop them when every word is technical.
                var words = s.Text.Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 1 && words.All(IsTechnical)) continue;
                string t = s.Text.Trim();
                if (t.Length <= 5 && t.All(c => char.IsUpper(c) || char.IsDigit(c)) && s.Note?.Contains("sigla") != true)
                {
                    s.Include = false;
                    s.Note = (s.Note is null ? "" : s.Note + "; ") + "sigla, desmarcada por padrão";
                }
            }
            yield return s;
        }
    }
}
