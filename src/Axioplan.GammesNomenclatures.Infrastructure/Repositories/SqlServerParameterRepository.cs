using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerParameterRepository(
    IOptions<DatabaseOptions> options,
    SqlApplicationLogger sqlLogger) : IParameterRepository
{
    public async Task<IReadOnlyList<ParameterFamilyItem>> GetFamiliesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT code, label FROM product_families ORDER BY label";

        var items = new List<ParameterFamilyItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ParameterFamilyItem(reader.GetString(0), reader.GetString(1)));
        }

        return items;
    }

    public async Task<IReadOnlyList<CoefficientItem>> GetConsumptionCoefficientsAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        return await LoadCoefficientsAsync("consumption_coefficients", familyCode, cancellationToken);
    }

    public async Task<IReadOnlyList<CoefficientItem>> GetTimeCoefficientsAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        return await LoadCoefficientsAsync("time_coefficients", familyCode, cancellationToken);
    }

    public async Task<IReadOnlyList<BomLossRateItem>> GetBomLossRatesAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bl.id, bl.line_no, bb.code, a.code, a.label, bl.quantity_base, bl.unit, bl.behavior, bl.loss_rate
            FROM bom_base_lines bl
            JOIN bom_bases bb ON bb.id = bl.bom_base_id
            JOIN product_families pf ON pf.id = bb.product_family_id
            JOIN articles a ON a.id = bl.component_article_id
            WHERE pf.code = @familyCode
              AND bb.id = (
                  SELECT TOP 1 bb2.id
                  FROM bom_bases bb2
                  WHERE bb2.product_family_id = pf.id
                  ORDER BY bb2.version DESC, bb2.id DESC
              )
            ORDER BY bl.line_no
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var items = new List<BomLossRateItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new BomLossRateItem(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDouble(8)));
        }

        return items;
    }

    public async Task<IReadOnlyList<AttributeOptionAdminItem>> GetAttributeOptionsAsync(
        string attributeCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ao.id, ad.code, ad.label, ao.technical_code, ao.display_value, ao.normalized_value, ao.status
            FROM attribute_options ao
            JOIN attribute_definitions ad ON ad.id = ao.attribute_id
            WHERE ad.code = @attributeCode
            ORDER BY CASE ao.status WHEN 'VALIDATED' THEN 0 WHEN 'TO_VALIDATE' THEN 1 ELSE 2 END,
                     ao.display_value
            """;
        command.Parameters.AddWithValue("@attributeCode", attributeCode);

        var items = new List<AttributeOptionAdminItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AttributeOptionAdminItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return items;
    }

    public async Task<IReadOnlyList<MvpParameterItem>> GetMvpParametersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, g.code, g.label, p.code, p.label, p.value_type, p.scope, p.description,
                   COALESCE(v.value_text, '')
            FROM mvp_parameters p
            JOIN mvp_parameter_groups g ON g.id = p.group_id
            LEFT JOIN mvp_parameter_values v ON v.parameter_id = p.id AND v.scope_key IS NULL
            ORDER BY g.position, p.code
            """;

        var items = new List<MvpParameterItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new MvpParameterItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8)));
        }

        return items;
    }

    public async Task CreateAttributeOptionAsync(
        CreateParameterAttributeOptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var formattedValue = NormalizeOptionValue(request.Value);
        var technicalCode = formattedValue.ToUpperInvariant();
        var displayValue = formattedValue;
        var normalizedValue = formattedValue.ToUpperInvariant();

        await using var connection = OpenConnection();

        var attributeId = await LoadAttributeIdAsync(connection, request.AttributeCode, cancellationToken);

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT id, status
                FROM attribute_options
                WHERE attribute_id = @attributeId AND normalized_value = @normalizedValue
                """;
            check.Parameters.AddWithValue("@attributeId", attributeId);
            check.Parameters.AddWithValue("@normalizedValue", normalizedValue);

            await using var reader = await check.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingId = reader.GetInt32(0);
                var existingStatus = reader.GetString(1);
                if (existingStatus == "OBSOLETE")
                {
                    await reader.CloseAsync();
                    await ReactivateAttributeOptionAsync(connection, existingId, displayValue, technicalCode, cancellationToken);
                    await CreateDefaultCoefficientsForNewOptionAsync(connection, request.AttributeCode, technicalCode, cancellationToken);
                    await sqlLogger.LogAsync("INFO", "Parameters", $"Option {request.AttributeCode} reactivee", technicalCode, cancellationToken);
                    await MarkCbnRecalcIfEnabledAsync($"attribute_option_reactivate:{request.AttributeCode}:{technicalCode}", cancellationToken);
                    return;
                }

                throw new InvalidOperationException(
                    $"L'option {request.AttributeCode} '{formattedValue}' existe deja.");
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
                VALUES (@attributeId, @displayValue, @normalizedValue, @technicalCode, 'VALIDATED')
                """;
            insert.Parameters.AddWithValue("@attributeId", attributeId);
            insert.Parameters.AddWithValue("@displayValue", displayValue);
            insert.Parameters.AddWithValue("@normalizedValue", normalizedValue);
            insert.Parameters.AddWithValue("@technicalCode", technicalCode);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await CreateDefaultCoefficientsForNewOptionAsync(connection, request.AttributeCode, technicalCode, cancellationToken);
        await sqlLogger.LogAsync("INFO", "Parameters", $"Option {request.AttributeCode} creee", technicalCode, cancellationToken);
        await MarkCbnRecalcIfEnabledAsync($"attribute_option_create:{request.AttributeCode}:{technicalCode}", cancellationToken);
    }

    public async Task UpdateAttributeOptionStatusAsync(
        UpdateParameterAttributeOptionStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Status is not ("VALIDATED" or "BLOCKED"))
        {
            throw new InvalidOperationException("Statut option invalide. Utilisez VALIDATED ou BLOCKED.");
        }

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE attribute_options
            SET status = @status
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@status", request.Status);
        command.Parameters.AddWithValue("@id", request.OptionId);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException($"Option introuvable : {request.OptionId}");
        }

        await sqlLogger.LogAsync("INFO", "Parameters", $"Statut option mis a jour", $"{request.OptionId}:{request.Status}", cancellationToken);
        await MarkCbnRecalcIfEnabledAsync($"attribute_option_status:{request.OptionId}:{request.Status}", cancellationToken);
    }

    public async Task UpdateConsumptionCoefficientAsync(
        UpdateCoefficientRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCoefficient(request.Coefficient);
        await UpdateCoefficientRowAsync("consumption_coefficients", request.Id, request.Coefficient, cancellationToken);
        await MarkCbnRecalcIfEnabledAsync("consumption_coefficients", cancellationToken);
    }

    public async Task UpdateTimeCoefficientAsync(
        UpdateCoefficientRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCoefficient(request.Coefficient);
        await UpdateCoefficientRowAsync("time_coefficients", request.Id, request.Coefficient, cancellationToken);
        await MarkCbnRecalcIfEnabledAsync("time_coefficients", cancellationToken);
    }

    public async Task UpdateBomLossRateAsync(
        UpdateLossRateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.LossRate < 0 || request.LossRate >= 1)
        {
            throw new InvalidOperationException("Le taux de perte doit etre compris entre 0 et 1 (exclus).");
        }

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE bom_base_lines SET loss_rate = @lossRate WHERE id = @id";
        command.Parameters.AddWithValue("@lossRate", request.LossRate);
        command.Parameters.AddWithValue("@id", request.LineId);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException($"Ligne BOM introuvable : {request.LineId}");
        }

        await MarkCbnRecalcIfEnabledAsync("bom_loss_rate", cancellationToken);
    }

    public async Task UpdateMvpParameterAsync(
        UpdateMvpParameterRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            MERGE mvp_parameter_values AS target
            USING (
                SELECT p.id AS parameter_id
                FROM mvp_parameters p
                WHERE p.code = @parameterCode
            ) AS source
            ON target.parameter_id = source.parameter_id
               AND ((target.scope_key IS NULL AND @scopeKey IS NULL) OR target.scope_key = @scopeKey)
            WHEN MATCHED THEN
                UPDATE SET value_text = @value, updated_at = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (parameter_id, scope_key, value_text)
                VALUES (source.parameter_id, @scopeKey, @value);
            """;
        command.Parameters.AddWithValue("@parameterCode", request.ParameterCode);
        command.Parameters.AddWithValue("@value", request.Value);
        command.Parameters.AddWithValue("@scopeKey", (object?)request.ScopeKey ?? DBNull.Value);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException($"Parametre introuvable : {request.ParameterCode}");
        }

        await MarkCbnRecalcIfEnabledAsync(request.ParameterCode, cancellationToken);
    }

    private async Task<IReadOnlyList<CoefficientItem>> LoadCoefficientsAsync(
        string tableName,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT cc.id, ad.code, ad.label, ao.technical_code, ao.display_value,
                   cc.coefficient, cc.status, cc.source_status
            FROM {tableName} cc
            JOIN product_families pf ON pf.id = cc.product_family_id
            JOIN attribute_definitions ad ON ad.id = cc.attribute_id
            JOIN attribute_options ao ON ao.id = cc.option_id
            WHERE pf.code = @familyCode
            ORDER BY ad.code, ao.technical_code
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var items = new List<CoefficientItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CoefficientItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return items;
    }

    private async Task UpdateCoefficientRowAsync(
        string tableName,
        int id,
        double coefficient,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {tableName}
            SET coefficient = @coefficient
            WHERE id = @id AND status = 'VALIDATED'
            """;
        command.Parameters.AddWithValue("@coefficient", coefficient);
        command.Parameters.AddWithValue("@id", id);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException(
                $"Coefficient introuvable ou non valide (id={id}). Seuls les coefficients VALIDATED sont modifiables.");
        }
    }

    private static string NormalizeOptionValue(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("La valeur ne peut pas etre vide.");
        }

        return trimmed;
    }

    private static async Task ReactivateAttributeOptionAsync(
        SqlConnection connection,
        int optionId,
        string displayValue,
        string technicalCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE attribute_options
            SET display_value = @displayValue,
                technical_code = @technicalCode,
                status = 'VALIDATED'
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", optionId);
        command.Parameters.AddWithValue("@displayValue", displayValue);
        command.Parameters.AddWithValue("@technicalCode", technicalCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> LoadAttributeIdAsync(
        SqlConnection connection,
        string attributeCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM attribute_definitions WHERE code = @code";
        command.Parameters.AddWithValue("@code", attributeCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not int attributeId)
        {
            throw new InvalidOperationException($"Attribut introuvable : {attributeCode}");
        }

        return attributeId;
    }

    private static async Task CreateDefaultCoefficientsForNewOptionAsync(
        SqlConnection connection,
        string attributeCode,
        string technicalCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @attributeId INT = (SELECT id FROM attribute_definitions WHERE code = @attributeCode);
            DECLARE @optionId INT = (
                SELECT id
                FROM attribute_options
                WHERE attribute_id = @attributeId AND technical_code = @technicalCode
            );

            INSERT INTO consumption_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
            SELECT pf.id, @attributeId, @optionId, 1.0, 'SIMULATED', 'VALIDATED',
                   'Coefficient cree automatiquement pour nouvelle option.'
            FROM product_families pf
            WHERE NOT EXISTS (
                SELECT 1
                FROM consumption_coefficients cc
                WHERE cc.product_family_id = pf.id
                  AND cc.attribute_id = @attributeId
                  AND cc.option_id = @optionId
            );

            INSERT INTO time_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
            SELECT pf.id, @attributeId, @optionId, 1.0, 'SIMULATED', 'VALIDATED',
                   'Coefficient cree automatiquement pour nouvelle option.'
            FROM product_families pf
            WHERE NOT EXISTS (
                SELECT 1
                FROM time_coefficients tc
                WHERE tc.product_family_id = pf.id
                  AND tc.attribute_id = @attributeId
                  AND tc.option_id = @optionId
            );
            """;
        command.Parameters.AddWithValue("@attributeCode", attributeCode);
        command.Parameters.AddWithValue("@technicalCode", technicalCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateCoefficient(double coefficient)
    {
        if (coefficient <= 0)
        {
            throw new InvalidOperationException("Le coefficient doit etre strictement positif.");
        }
    }

    private async Task MarkCbnRecalcIfEnabledAsync(string reason, CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pv.value_text
            FROM mvp_parameters p
            JOIN mvp_parameter_values pv ON pv.parameter_id = p.id AND pv.scope_key IS NULL
            WHERE p.code = 'CBN_AUTO_RECALC_ON_PARAM_CHANGE'
            """;
        var flag = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (!string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE cbn_runs
            SET notes = COALESCE(notes + '; ', '') + 'RECALC_PENDING:' + @reason
            WHERE status = 'COMPLETED'
            """;
        update.Parameters.AddWithValue("@reason", reason);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await sqlLogger.LogAsync("INFO", "CBN", "Recalcul CBN marque apres changement parametre", reason, cancellationToken);
    }

    private SqlConnection OpenConnection()
    {
        var connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Chaine de connexion SQL Server introuvable. Configurez Database:ConnectionString.");
        }

        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    public async Task<FormulaConfiguratorState> GetFormulaConfiguratorAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        await EnsureFormulaSchemaAsync(cancellationToken);
        await EnsureConsumptionCoefficientsForFamilyAsync(familyCode, cancellationToken);
        var formula = await GetRequirementFormulaAsync(familyCode, cancellationToken)
            ?? new RequirementFormulaItem(null, familyCode, "BesoinBase * SIZE * COLOR", "Besoin de base * Taille * Couleur", true, "VALIDATED");
        var arguments = await LoadCalculationArgumentsAsync(familyCode, cancellationToken);
        return new FormulaConfiguratorState(familyCode, formula, arguments);
    }

    public async Task<RequirementFormulaItem?> GetRequirementFormulaAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        await EnsureFormulaSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rf.id, pf.code, rf.expression, rf.display_expression, rf.apply_order_quantity, rf.status
            FROM requirement_formulas rf
            JOIN product_families pf ON pf.id = rf.product_family_id
            WHERE pf.code = @familyCode AND rf.target = 'REQUIREMENT'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RequirementFormulaItem(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetString(5));
    }

    public async Task SaveRequirementFormulaAsync(
        SaveRequirementFormulaRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFormulaSchemaAsync(cancellationToken);
        var expression = FormulaEngine.BuildDisplayExpression(request.Tokens);
        var arguments = await LoadCalculationArgumentsAsync(request.FamilyCode, cancellationToken);
        var allowed = arguments.Where(argument => argument.IsActive).Select(argument => argument.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        FormulaEngine.Validate(expression, allowed);

        var displayExpression = BuildFriendlyDisplayExpression(request.Tokens, arguments);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            MERGE requirement_formulas AS target
            USING (
                SELECT pf.id AS product_family_id
                FROM product_families pf
                WHERE pf.code = @familyCode
            ) AS source
            ON target.product_family_id = source.product_family_id AND target.target = 'REQUIREMENT'
            WHEN MATCHED THEN
                UPDATE SET
                    expression = @expression,
                    display_expression = @displayExpression,
                    apply_order_quantity = @applyOrderQuantity,
                    status = 'VALIDATED',
                    updated_at = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (product_family_id, target, expression, display_expression, apply_order_quantity, status)
                VALUES (source.product_family_id, 'REQUIREMENT', @expression, @displayExpression, @applyOrderQuantity, 'VALIDATED');
            """;
        command.Parameters.AddWithValue("@familyCode", request.FamilyCode);
        command.Parameters.AddWithValue("@expression", expression);
        command.Parameters.AddWithValue("@displayExpression", displayExpression);
        command.Parameters.AddWithValue("@applyOrderQuantity", request.ApplyOrderQuantity);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await MarkCbnRecalcIfEnabledAsync("requirement_formulas", cancellationToken);
    }

    public async Task<int> CreateCalculationArgumentAsync(
        CreateCalculationArgumentRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFormulaSchemaAsync(cancellationToken);
        var code = request.Code.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Le code argument est obligatoire.");
        }

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF EXISTS (SELECT 1 FROM attribute_definitions WHERE code = @code)
            BEGIN
                UPDATE attribute_definitions
                SET label = @label,
                    is_formula_argument = 1,
                    is_active = 1
                WHERE code = @code;

                SELECT id FROM attribute_definitions WHERE code = @code;
            END
            ELSE
            BEGIN
                INSERT INTO attribute_definitions (code, label, value_type, is_generator, is_formula_argument, is_active, selection_mode)
                OUTPUT INSERTED.id
                VALUES (@code, @label, 'OPTION', 0, 1, 1, 'CONTROLLED_WITH_CREATE');
            END
            """;
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@label", request.Label.Trim());
        var id = (int)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Impossible de creer l'argument."));
        await sqlLogger.LogAsync("INFO", "Parametres", "Argument formule cree", code, cancellationToken);
        return id;
    }

    public async Task UpdateCalculationArgumentAsync(
        UpdateCalculationArgumentRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFormulaSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE attribute_definitions
            SET label = @label,
                is_active = @isActive
            WHERE id = @id AND is_formula_argument = 1
            """;
        command.Parameters.AddWithValue("@id", request.ArgumentId);
        command.Parameters.AddWithValue("@label", request.Label.Trim());
        command.Parameters.AddWithValue("@isActive", request.IsActive);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException("Argument de formule introuvable.");
        }

        await MarkCbnRecalcIfEnabledAsync("attribute_definitions", cancellationToken);
    }

    public Task CreateArgumentValueAsync(CreateArgumentValueRequest request, CancellationToken cancellationToken = default)
        => CreateAttributeOptionAsync(new CreateParameterAttributeOptionRequest(request.AttributeCode, request.Value), cancellationToken);

    public async Task UpdateArgumentValueCoefficientAsync(
        UpdateArgumentValueCoefficientRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCoefficient(request.Coefficient);
        await UpdateCoefficientRowAsync("consumption_coefficients", request.CoefficientId, request.Coefficient, cancellationToken);
        await MarkCbnRecalcIfEnabledAsync("consumption_coefficients", cancellationToken);
    }

    public Task UpdateArgumentValueStatusAsync(UpdateArgumentValueStatusRequest request, CancellationToken cancellationToken = default)
        => UpdateAttributeOptionStatusAsync(new UpdateParameterAttributeOptionStatusRequest(request.OptionId, request.Status), cancellationToken);

    public async Task DeleteArgumentValueAsync(
        DeleteArgumentValueRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFormulaSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();

        await using (var usageCheck = connection.CreateCommand())
        {
            usageCheck.CommandText = """
                SELECT COUNT(*)
                FROM article_attribute_values aav
                JOIN attribute_options ao ON ao.id = aav.option_id
                JOIN attribute_definitions ad ON ad.id = ao.attribute_id
                WHERE aav.option_id = @optionId
                  AND ad.is_formula_argument = 1
                """;
            usageCheck.Parameters.AddWithValue("@optionId", request.OptionId);
            if (Convert.ToInt32(await usageCheck.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                throw new InvalidOperationException(
                    "Cette valeur est utilisee par des articles. Utilisez Bloquer pour la desactiver.");
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ao
            SET status = 'OBSOLETE'
            FROM attribute_options ao
            JOIN attribute_definitions ad ON ad.id = ao.attribute_id
            WHERE ao.id = @optionId
              AND ad.is_formula_argument = 1
            """;
        command.Parameters.AddWithValue("@optionId", request.OptionId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException("Valeur d'argument introuvable.");
        }

        await sqlLogger.LogAsync("INFO", "Parametres", "Valeur argument supprimee", request.OptionId.ToString(), cancellationToken);
        await MarkCbnRecalcIfEnabledAsync($"attribute_option_delete:{request.OptionId}", cancellationToken);
    }

    public async Task DeleteCalculationArgumentAsync(
        DeleteCalculationArgumentRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFormulaSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();

        string? code;
        await using (var load = connection.CreateCommand())
        {
            load.CommandText = """
                SELECT code
                FROM attribute_definitions
                WHERE id = @id AND is_formula_argument = 1
                """;
            load.Parameters.AddWithValue("@id", request.ArgumentId);
            code = await load.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Argument de formule introuvable.");
        }

        if (code is "SIZE" or "COLOR" or "GAUGE")
        {
            throw new InvalidOperationException(
                $"L'argument systeme {code} ne peut pas etre supprime. Utilisez Desactiver.");
        }

        var formula = await GetRequirementFormulaAsync(request.FamilyCode, cancellationToken);
        if (formula is not null
            && FormulaEngine.Tokenize(formula.Expression)
                .Any(token => string.Equals(token, code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Retirez {code} de la formule avant de supprimer cet argument.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE attribute_definitions
            SET is_formula_argument = 0,
                is_active = 0
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", request.ArgumentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await sqlLogger.LogAsync("INFO", "Parametres", "Argument formule supprime", code, cancellationToken);
        await MarkCbnRecalcIfEnabledAsync("attribute_definitions", cancellationToken);
    }

    private async Task<IReadOnlyList<CalculationArgumentItem>> LoadCalculationArgumentsAsync(
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ad.id, ad.code, ad.label, ad.is_active
            FROM attribute_definitions ad
            WHERE ad.is_formula_argument = 1
            ORDER BY CASE ad.code WHEN 'SIZE' THEN 0 WHEN 'COLOR' THEN 1 WHEN 'GAUGE' THEN 2 ELSE 3 END, ad.label
            """;

        var argumentRows = new List<(int Id, string Code, string Label, bool IsActive)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                argumentRows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
            }
        }

        var arguments = new List<CalculationArgumentItem>();
        foreach (var row in argumentRows)
        {
            var values = await LoadArgumentValuesAsync(connection, familyCode, row.Code, cancellationToken);
            arguments.Add(new CalculationArgumentItem(row.Id, row.Code, row.Label, row.IsActive, values));
        }

        return arguments;
    }

    private static async Task<IReadOnlyList<CalculationArgumentValueItem>> LoadArgumentValuesAsync(
        SqlConnection connection,
        string familyCode,
        string attributeCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ao.id, cc.id, ao.display_value, ao.technical_code, cc.coefficient, ao.status
            FROM attribute_options ao
            JOIN attribute_definitions ad ON ad.id = ao.attribute_id
            LEFT JOIN consumption_coefficients cc ON cc.option_id = ao.id
                AND cc.product_family_id = (SELECT id FROM product_families WHERE code = @familyCode)
            WHERE ad.code = @attributeCode
              AND ao.status <> 'OBSOLETE'
            ORDER BY ao.display_value
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        command.Parameters.AddWithValue("@attributeCode", attributeCode);

        var values = new List<CalculationArgumentValueItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new CalculationArgumentValueItem(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? 1.0 : reader.GetDouble(4),
                reader.GetString(5)));
        }

        return values;
    }

    private static string BuildFriendlyDisplayExpression(
        IReadOnlyList<string> tokens,
        IReadOnlyList<CalculationArgumentItem> arguments)
    {
        var labels = arguments.ToDictionary(argument => argument.Code, argument => argument.Label, StringComparer.OrdinalIgnoreCase);
        var friendly = tokens.Select(token =>
        {
            if (string.Equals(token, FormulaEngine.BesoinBaseToken, StringComparison.OrdinalIgnoreCase))
            {
                return "Besoin de base";
            }

            return labels.TryGetValue(token, out var label) ? label : token;
        });
        return string.Join(' ', friendly);
    }

    private async Task EnsureConsumptionCoefficientsForFamilyAsync(
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO consumption_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
            SELECT pf.id, ad.id, ao.id, 1.0, 'CONFIRMED', 'VALIDATED', 'Coefficient auto argument formule'
            FROM product_families pf
            JOIN attribute_definitions ad ON ad.is_formula_argument = 1
            JOIN attribute_options ao ON ao.attribute_id = ad.id
            WHERE pf.code = @familyCode
              AND ao.status <> 'OBSOLETE'
              AND NOT EXISTS (
                  SELECT 1
                  FROM consumption_coefficients cc
                  WHERE cc.product_family_id = pf.id
                    AND cc.attribute_id = ad.id
                    AND cc.option_id = ao.id
              )
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureFormulaSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF COL_LENGTH('attribute_definitions', 'is_formula_argument') IS NULL
            BEGIN
                ALTER TABLE attribute_definitions ADD is_formula_argument BIT NOT NULL CONSTRAINT DF_attribute_definitions_is_formula_argument DEFAULT 0;
            END;

            IF COL_LENGTH('attribute_definitions', 'is_active') IS NULL
            BEGIN
                ALTER TABLE attribute_definitions ADD is_active BIT NOT NULL CONSTRAINT DF_attribute_definitions_is_active DEFAULT 1;
            END;

            IF OBJECT_ID(N'dbo.requirement_formulas', N'U') IS NULL
            BEGIN
                CREATE TABLE requirement_formulas (
                    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    product_family_id INT NOT NULL,
                    target NVARCHAR(30) NOT NULL DEFAULT 'REQUIREMENT',
                    expression NVARCHAR(500) NOT NULL,
                    display_expression NVARCHAR(500) NOT NULL,
                    apply_order_quantity BIT NOT NULL DEFAULT 1,
                    status NVARCHAR(20) NOT NULL DEFAULT 'VALIDATED',
                    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT uq_requirement_formulas_family_target UNIQUE (product_family_id, target),
                    FOREIGN KEY (product_family_id) REFERENCES product_families(id)
                );
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
