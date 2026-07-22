namespace Axioplan.GammesNomenclatures.Web.Components;

/// <summary>Libellés d'affichage UI uniquement — ne modifie pas les données métier.</summary>
public static class DisplayLabels
{
    public static string ProfileStatus(string status) => status switch
    {
        "VALIDATED" => "Validé",
        "DRAFT" => "Brouillon",
        "TO_VALIDATE" => "À valider",
        "BLOCKED" => "Bloqué",
        "OBSOLETE" => "Obsolète",
        _ => status,
    };

    public static string Color(string code) => code switch
    {
        "MINT" => "Mint",
        "NOIR" => "Noir",
        _ => code,
    };

    public static string Component(string code) => code switch
    {
        "FIL-MINT" => "Fil couleur mint",
        "VCOMP-STD" => "Vignette composition",
        "SACHET-STD" => "Sachet standard",
        "TISSU_BASE" => "Tissu base",
        "TISSU_NOIR" => "Tissu noir",
        "TISSU_BLEU" => "Tissu bleu",
        "FIL" => "Fil",
        "BOUTON" => "Bouton",
        "ZIP" => "Zip",
        "EMBALLAGE" => "Emballage",
        _ => code,
    };

    public static string Unit(string unit) => unit switch
    {
        "KG" => "kg",
        "PIECE" => "pièce",
        "TO_CONFIRM" => "à confirmer avec le métier",
        _ => unit,
    };

    public static string TimeUnit(string unit)
    {
        if (string.Equals(unit, "TO_CONFIRM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(unit, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return "à confirmer avec le métier";
        }

        return Unit(unit);
    }

    public static string ObjectType(string type) => type switch
    {
        "BOM_LINE" => "Ligne nomenclature",
        "ROUTING_OPERATION" => "Opération gamme",
        _ => type,
    };

    public static string BomBase(string? code) => code switch
    {
        "BOM_BASE_PULL_COL_ROND_EXEMPLE" => "Nomenclature pull col rond (exemple)",
        _ => code ?? "—",
    };

    public static string RoutingBase(string? code) => code switch
    {
        "GAM_BASE_PULL_COL_ROND_PROD_EXEMPLE" => "Gamme pull col rond (fichier prod)",
        _ => code ?? "—",
    };

    public static string WorkcentersSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        return string.Join(", ", summary.Split(',', StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                var segments = part.Split('=', 2);
                if (segments.Length != 2)
                {
                    return part;
                }

                var center = segments[0].Replace("WU-", "Centre ");
                return $"{center} : {segments[1]}";
            }));
    }

    public static string ArticleType(string type) => type switch
    {
        "FINISHED_GOOD" => "Produit fini",
        "SEMI_FINISHED" => "Semi-fini",
        "COMPONENT" => "Composant",
        "SERVICE" => "Service",
        _ => type,
    };

    public static string CreationMode(string mode) => mode switch
    {
        "MANUAL" => "Manuel",
        "CONFIGURED" => "Configuré",
        "DUPLICATED" => "Dupliqué",
        "IMPORTED" => "Importé",
        _ => mode,
    };

    public static string SelectionMode(string mode) => mode switch
    {
        "CONTROLLED" => "Sélection contrôlée",
        "CONTROLLED_WITH_CREATE" => "Sélection + création",
        "FREE_TEXT" => "Texte libre",
        _ => mode,
    };

    public static string OptionStatus(string status) => status switch
    {
        "DRAFT" => "Brouillon",
        "TO_VALIDATE" => "À valider",
        "VALIDATED" => "Validée",
        "BLOCKED" => "Bloquée",
        "MERGED" => "Fusionnée",
        _ => status,
    };

    public static string CalculationMode(string mode) => mode switch
    {
        "VARIABLE_MATERIAL" => "Matiere variable",
        "FIXED_SUPPLY" => "Fourniture fixe",
        "TO_CONFIRM" => "A confirmer",
        _ => mode,
    };
}
