using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerSimulationRepository(IOptions<DatabaseOptions> options) : ISimulationRepository
{
    private sealed record BomLine(
        int LineNo,
        string ComponentCode,
        double QuantityBase,
        string Unit,
        double LossRate,
        string Behavior);

    private sealed record RoutingOperation(
        int OperationNo,
        string Name,
        string WorkcenterCode,
        double TimeBase,
        string TimeUnit,
        string Behavior);

    private sealed record CalculationTraceDraft(
        string ObjectType,
        string VariantCode,
        string RuleCode,
        string SourceLabel,
        double BaseValue,
        double SizeCoefficient,
        double ColorCoefficient,
        double LossRate,
        double ResultValue,
        string Unit,
        string Formula);

    public async Task<IReadOnlyList<FamilyListItem>> GetFamiliesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT code, label
            FROM product_families
            ORDER BY label
            """;

        var families = new List<FamilyListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            families.Add(new FamilyListItem(reader.GetString(0), reader.GetString(1)));
        }

        return families;
    }

    public async Task<IReadOnlyList<ProfileListItem>> GetProfilesAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT gp.code, gp.label, gp.status, gp.can_generate_bom, gp.can_generate_routing
            FROM generation_profiles gp
            JOIN product_families pf ON pf.id = gp.product_family_id
            WHERE pf.code = @familyCode
            ORDER BY gp.label
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var profiles = new List<ProfileListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new ProfileListItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4)));
        }

        return profiles;
    }

    public async Task<IReadOnlyList<AttributeOptionListItem>> GetSizeOptionsAsync(CancellationToken cancellationToken = default)
        => await LoadAttributeOptionsAsync("SIZE", cancellationToken);

    public async Task<IReadOnlyList<AttributeOptionListItem>> GetColorOptionsAsync(CancellationToken cancellationToken = default)
        => await LoadAttributeOptionsAsync("COLOR", cancellationToken);

    public async Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Sizes.Count == 0 || request.Colors.Count == 0)
        {
            throw new InvalidOperationException("Selectionnez au moins une taille et une couleur.");
        }

        await using var connection = OpenConnection();

        var family = await LoadFamilyAsync(connection, request.FamilyCode, cancellationToken)
            ?? throw new InvalidOperationException($"Famille introuvable : {request.FamilyCode}");

        var profile = await LoadProfileAsync(connection, request.ProfileCode, cancellationToken)
            ?? throw new InvalidOperationException($"Profil introuvable : {request.ProfileCode}");

        var baseArticle = await LoadBaseArticleCodeAsync(connection, request.FamilyCode, cancellationToken);
        var bomLines = await LoadBomLinesAsync(connection, request.FamilyCode, cancellationToken);
        var routingOps = await LoadRoutingOperationsAsync(connection, request.FamilyCode, cancellationToken);
        var requirementFormula = await LoadRequirementFormulaAsync(connection, request.FamilyCode, cancellationToken);
        var variants = GenerationEngine.BuildSizeColorVariants(baseArticle, request.Sizes, request.Colors);

        var variantResults = new List<VariantResult>();
        var bomSummary = new List<BomSummaryRow>();
        var componentTotals = new Dictionary<string, (double Net, double Gross, string Unit)>();
        var routingSummary = new List<RoutingSummaryRow>();
        var allTraces = new List<CalculationTraceDraft>();

        for (var index = 0; index < variants.Count; index++)
        {
            var variant = variants[index];
            var shortCode = variant.ArticleCode.Replace($"{baseArticle}-", string.Empty);
            variantResults.Add(new VariantResult(index + 1, variant.ArticleCode, shortCode, variant.SizeCode, variant.ColorCode));

            var sizeCoeff = await LoadCoefficientAsync(
                connection, request.FamilyCode, "SIZE", variant.SizeCode, "consumption_coefficients", cancellationToken);
            var colorCoeff = await LoadCoefficientAsync(
                connection, request.FamilyCode, "COLOR", variant.ColorCode, "consumption_coefficients", cancellationToken);
            var argumentCoeffs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["SIZE"] = sizeCoeff,
                ["COLOR"] = colorCoeff
            };

            foreach (var line in bomLines)
            {
                var (net, gross, trace) = ComputeBomLine(line, argumentCoeffs, requirementFormula);
                bomSummary.Add(new BomSummaryRow(shortCode, line.ComponentCode, net, gross, line.Unit));

                if (!componentTotals.TryGetValue(line.ComponentCode, out var totals))
                {
                    totals = (0, 0, line.Unit);
                }

                componentTotals[line.ComponentCode] = (totals.Net + net, totals.Gross + gross, line.Unit);

                if (trace is not null)
                {
                    allTraces.Add(trace with { VariantCode = variant.ArticleCode });
                }
            }

            var timeSizeCoeff = await LoadCoefficientAsync(
                connection, request.FamilyCode, "SIZE", variant.SizeCode, "time_coefficients", cancellationToken);
            var timeColorCoeff = await LoadCoefficientAsync(
                connection, request.FamilyCode, "COLOR", variant.ColorCode, "time_coefficients", cancellationToken);

            var byWorkcenter = new Dictionary<string, double>();
            var totalTime = 0.0;

            foreach (var op in routingOps)
            {
                var generatedTime = GenerationEngine.CalculateOperationTime(op.TimeBase, timeSizeCoeff, timeColorCoeff);
                byWorkcenter[op.WorkcenterCode] = byWorkcenter.GetValueOrDefault(op.WorkcenterCode) + generatedTime;
                totalTime += generatedTime;

                if (op.OperationNo == 110)
                {
                    allTraces.Add(new CalculationTraceDraft(
                        "ROUTING_OPERATION",
                        variant.ArticleCode,
                        "ROUTING_COPY_TO_VALIDATE_MVP",
                        $"{op.Name} (op. {op.OperationNo})",
                        op.TimeBase,
                        timeSizeCoeff,
                        timeColorCoeff,
                        0.0,
                        generatedTime,
                        op.TimeUnit,
                        "base * size * color"));
                }
            }

            var topCenters = byWorkcenter
                .OrderByDescending(pair => pair.Value)
                .Take(3)
                .Select(pair => $"{pair.Key}={pair.Value:F1}");
            routingSummary.Add(new RoutingSummaryRow(shortCode, totalTime, string.Join(", ", topCenters)));
        }

        var shownVariants = new HashSet<string>
        {
            $"{baseArticle}-L-NOIR",
            $"{baseArticle}-S-MINT",
        };

        var traces = allTraces
            .Where(trace =>
                (trace.ObjectType == "BOM_LINE" && trace.VariantCode == $"{baseArticle}-L-NOIR")
                || (trace.ObjectType == "ROUTING_OPERATION" && shownVariants.Contains(trace.VariantCode)))
            .Select(trace => new CalculationTraceResult(
                trace.ObjectType,
                trace.VariantCode.Replace($"{baseArticle}-", string.Empty),
                trace.RuleCode,
                trace.SourceLabel,
                trace.BaseValue,
                trace.SizeCoefficient,
                trace.ColorCoefficient,
                trace.LossRate,
                trace.ResultValue,
                trace.Unit,
                trace.Formula))
            .ToList();

        return new SimulationResult
        {
            Family = family,
            Profile = profile,
            ProfileCanGenerate = GenerationEngine.ProfileCanGenerate(profile.Status),
            OperationCount = routingOps.Count,
            TimeUnit = routingOps.FirstOrDefault()?.TimeUnit ?? "N/A",
            Variants = variantResults,
            BomSummary = bomSummary,
            ComponentTotals = componentTotals
                .Select(pair => new ComponentTotal(pair.Key, pair.Value.Net, pair.Value.Gross, pair.Value.Unit))
                .ToList(),
            RoutingSummary = routingSummary,
            Traces = traces,
        };
    }

    private SqlConnection OpenConnection()
    {
        var connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Chaine de connexion SQL Server introuvable. Configurez Database:ConnectionString ou lancez : python scripts/init_db.py");
        }

        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static (double Net, double Gross, CalculationTraceDraft? Trace) ComputeBomLine(
        BomLine line,
        IReadOnlyDictionary<string, double> argumentCoefficients,
        string? formulaExpression)
    {
        if (line.Behavior == "CALCULATED")
        {
            var net = GenerationEngine.CalculateNetQuantityFromFormula(
                line.QuantityBase,
                formulaExpression,
                argumentCoefficients);
            var gross = GenerationEngine.CalculateGrossQuantity(net, line.LossRate);
            var trace = new CalculationTraceDraft(
                "BOM_LINE",
                string.Empty,
                $"BOM_LINE_MVP_{line.LineNo}",
                $"{line.ComponentCode} (ligne {line.LineNo})",
                line.QuantityBase,
                argumentCoefficients.TryGetValue("SIZE", out var sizeValue) ? sizeValue : 1.0,
                argumentCoefficients.TryGetValue("COLOR", out var colorValue) ? colorValue : 1.0,
                line.LossRate,
                gross,
                line.Unit,
                GenerationEngine.BuildFormulaTrace(
                    formulaExpression,
                    line.QuantityBase,
                    argumentCoefficients,
                    applyOrderQuantity: false,
                    orderQuantity: 1,
                    lossRate: line.LossRate));

            return (net, gross, trace);
        }

        var fixedNet = line.QuantityBase;
        var fixedGross = line.LossRate > 0
            ? GenerationEngine.CalculateGrossQuantity(fixedNet, line.LossRate)
            : fixedNet;

        return (fixedNet, fixedGross, null);
    }

    private static async Task<FamilyInfo?> LoadFamilyAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pf.code, pf.label, bb.code, rb.code
            FROM product_families pf
            LEFT JOIN bom_bases bb ON bb.id = (
                SELECT TOP 1 bb2.id
                FROM bom_bases bb2
                WHERE bb2.product_family_id = pf.id
                ORDER BY bb2.version DESC, bb2.id DESC
            )
            LEFT JOIN routing_bases rb ON rb.id = (
                SELECT TOP 1 rb2.id
                FROM routing_bases rb2
                WHERE rb2.product_family_id = pf.id
                ORDER BY rb2.version DESC, rb2.id DESC
            )
            WHERE pf.code = @familyCode
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        string code;
        string label;
        string? bomCode;
        string? routingCode;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            code = reader.GetString(0);
            label = reader.GetString(1);
            bomCode = reader.IsDBNull(2) ? null : reader.GetString(2);
            routingCode = reader.IsDBNull(3) ? null : reader.GetString(3);
        }

        var baseArticle = await LoadBaseArticleCodeAsync(connection, familyCode, cancellationToken);
        return new FamilyInfo(code, label, bomCode, routingCode, baseArticle);
    }

    private static async Task<ProfileInfo?> LoadProfileAsync(
        SqlConnection connection,
        string profileCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT code, label, status, can_generate_bom, can_generate_routing
            FROM generation_profiles
            WHERE code = @profileCode
            """;
        command.Parameters.AddWithValue("@profileCode", profileCode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ProfileInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4));
    }

    private static async Task<string> LoadBaseArticleCodeAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP 1 a.code
            FROM articles a
            JOIN article_families af ON af.id = a.family_id
            JOIN product_families pf ON pf.article_family_id = af.id
            WHERE pf.code = @familyCode AND a.article_type = 'FINISHED_GOOD'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string code ? code : "AH25PLUMIERE";
    }

    private static async Task<List<BomLine>> LoadBomLinesAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bl.line_no, a.code, bl.quantity_base, bl.unit, bl.loss_rate, bl.behavior
            FROM bom_base_lines bl
            JOIN bom_bases b ON b.id = bl.bom_base_id
            JOIN product_families pf ON pf.id = b.product_family_id
            JOIN articles a ON a.id = bl.component_article_id
            WHERE pf.code = @familyCode
              AND b.id = (
                  SELECT TOP 1 b2.id
                  FROM bom_bases b2
                  WHERE b2.product_family_id = pf.id
                  ORDER BY b2.version DESC, b2.id DESC
              )
            ORDER BY bl.line_no
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var lines = new List<BomLine>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new BomLine(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetDouble(2),
                reader.GetString(3),
                reader.GetDouble(4),
                reader.GetString(5)));
        }

        return lines;
    }

    private static async Task<List<RoutingOperation>> LoadRoutingOperationsAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ro.operation_no, ro.name, wc.code, ro.time_base, ro.time_unit, ro.behavior
            FROM routing_base_operations ro
            JOIN routing_bases rb ON rb.id = ro.routing_base_id
            JOIN product_families pf ON pf.id = rb.product_family_id
            JOIN workcenters wc ON wc.id = ro.workcenter_id
            WHERE pf.code = @familyCode
            ORDER BY ro.operation_no
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var operations = new List<RoutingOperation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            operations.Add(new RoutingOperation(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return operations;
    }

    private async Task<IReadOnlyList<AttributeOptionListItem>> LoadAttributeOptionsAsync(
        string attributeCode,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ao.technical_code, ao.display_value
            FROM attribute_options ao
            JOIN attribute_definitions ad ON ad.id = ao.attribute_id
            WHERE ad.code = @attributeCode
              AND ao.status = 'VALIDATED'
            ORDER BY ao.display_value
            """;
        command.Parameters.AddWithValue("@attributeCode", attributeCode);

        var options = new List<AttributeOptionListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            options.Add(new AttributeOptionListItem(reader.GetString(0), reader.GetString(1)));
        }

        return options;
    }

    private static async Task<double> LoadCoefficientAsync(
        SqlConnection connection,
        string familyCode,
        string attributeCode,
        string optionCode,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT cc.coefficient
            FROM {tableName} cc
            JOIN product_families pf ON pf.id = cc.product_family_id
            JOIN attribute_definitions ad ON ad.id = cc.attribute_id
            JOIN attribute_options ao ON ao.id = cc.option_id
            WHERE pf.code = @familyCode
              AND ad.code = @attributeCode
              AND ao.technical_code = @optionCode
              AND cc.status = 'VALIDATED'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        command.Parameters.AddWithValue("@attributeCode", attributeCode);
        command.Parameters.AddWithValue("@optionCode", optionCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is double coefficient ? coefficient : Convert.ToDouble(result ?? 1.0);
    }

    private static async Task<string?> LoadRequirementFormulaAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rf.expression
            FROM requirement_formulas rf
            JOIN product_families pf ON pf.id = rf.product_family_id
            WHERE pf.code = @familyCode
              AND rf.target = 'REQUIREMENT'
              AND rf.status = 'VALIDATED'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }
}
