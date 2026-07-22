using System.Security.Cryptography;
using System.Text;

namespace Axioplan.GammesNomenclatures.Domain.Aps.Compiler;

public static class ApsCompilerArtifactKinds
{
    public const string Needs = "BESOINS";
    public const string Loads = "CHARGES";
    public const string Hre = "HRE";
    public const string LeadTime = "DELAI";
}

public static class ApsCompilerStatuses
{
    public const string Valid = "VALID";
    public const string Invalid = "INVALID";
}

public static class ApsSegments
{
    public const string A = "SEG_A"; // tricotage (pieces <- fil)
    public const string B = "SEG_B"; // traitement -> emballage
    public const string C = "SEG_C"; // livraison / PF
}

/// <summary>
/// Cle de compilation : (article_racine, segment, chaine_de_circuits).
/// </summary>
public sealed record ApsCompileKey(
    string RootArticleCode,
    string Segment,
    string CircuitChain);

public sealed record ApsCompiledArtifact(
    long Id,
    ApsCompileKey Key,
    string ArtifactKind,
    string PayloadJson,
    string SourceHash,
    string Status,
    DateTime CompiledAtUtc,
    string Notes);

public sealed record ApsNeedLine(
    string ComponentCode,
    double Quantity,
    double OffsetDays,
    string BathConstraint,
    string SourcePath,
    string ConfirmationStatus);

public sealed record ApsLoadLine(
    string ResourceCode,
    string CapacityType,
    double UnitTime,
    double SetupProvision,
    string ConfirmationStatus);

public sealed record ApsHreLine(
    string ResourceCode,
    double HrePerUnit,
    string ConfirmationStatus);

public sealed record ApsLeadTimeLine(
    string OperationCode,
    string CircuitType,
    double LeadTimeDays,
    string? Milestone,
    string ConfirmationStatus);

public sealed record ApsCompileBundle(
    ApsCompileKey Key,
    string SourceHash,
    IReadOnlyList<ApsNeedLine> Needs,
    IReadOnlyList<ApsLoadLine> Loads,
    IReadOnlyList<ApsHreLine> Hre,
    IReadOnlyList<ApsLeadTimeLine> LeadTimes,
    string Notes);

/// <summary>
/// Hash deterministe des sources. Meme entree => meme hash (test T0).
/// </summary>
public static class ApsCompilerHash
{
    public static string Compute(params string?[] parts)
    {
        var joined = string.Join('|', parts.Select(p => (p ?? string.Empty).Trim().ToUpperInvariant()));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>
/// Squelette de compilation : architecture correcte, calculs partiels marques TO_CONFIRM.
/// </summary>
public static class ApsCompilerEngine
{
    public static ApsCompileBundle CompileSkeleton(
        ApsCompileKey key,
        string bomFingerprint,
        string routingFingerprint,
        string circuitsFingerprint,
        string yieldsFingerprint,
        string standardsFingerprint,
        string etaFingerprint,
        string unitsFingerprint,
        string decouplingFingerprint,
        IReadOnlyList<(string ComponentCode, double Qty, string? OperationCode)> bomLines,
        IReadOnlyList<(string OperationCode, string ResourceCode, double UnitMinutes, bool IsBottleneck)> routingOps)
    {
        var sourceHash = ApsCompilerHash.Compute(
            key.RootArticleCode,
            key.Segment,
            key.CircuitChain,
            bomFingerprint,
            routingFingerprint,
            circuitsFingerprint,
            yieldsFingerprint,
            standardsFingerprint,
            etaFingerprint,
            unitsFingerprint,
            decouplingFingerprint);

        var needs = bomLines.Select(line => new ApsNeedLine(
            line.ComponentCode,
            line.Qty,
            OffsetDays: 0, // TO_CONFIRM: deriver depuis operation_id + gamme
            BathConstraint: "LIBRE", // TO_CONFIRM
            SourcePath: $"{key.RootArticleCode}>{line.ComponentCode}",
            ConfirmationStatus: "TO_CONFIRM")).ToList();

        var loads = routingOps.Select(op => new ApsLoadLine(
            op.ResourceCode,
            CapacityType: "MACHINE", // TO_CONFIRM: resoudre type ressource
            UnitTime: op.UnitMinutes / 60.0,
            SetupProvision: 0, // TO_CONFIRM: provision moyenne journal
            ConfirmationStatus: "TO_CONFIRM")).ToList();

        var hre = routingOps
            .Where(op => op.IsBottleneck)
            .Select(op => new ApsHreLine(
                op.ResourceCode,
                HrePerUnit: op.UnitMinutes / 60.0, // TO_CONFIRM: / eta + courbe apprentissage
                ConfirmationStatus: "TO_CONFIRM"))
            .ToList();

        if (hre.Count == 0)
        {
            // Placeholder goulot remaillage — a remplacer par le vrai poste goulot.
            hre.Add(new ApsHreLine("REMAILLAGE", 0, "TO_CONFIRM"));
        }

        var leadTimes = routingOps.Select(op => new ApsLeadTimeLine(
            op.OperationCode,
            CircuitType: "INTERNE",
            LeadTimeDays: 0, // TO_CONFIRM: setup + q*tu/eta + transferts
            Milestone: null,
            ConfirmationStatus: "TO_CONFIRM")).ToList();

        return new ApsCompileBundle(
            key,
            sourceHash,
            needs,
            loads,
            hre,
            leadTimes,
            Notes: "Compilation squelette APS — offsets/rendements/HRE/delais partiels (TO_CONFIRM).");
    }
}
