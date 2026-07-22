using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models.Articles;
using Axioplan.GammesNomenclatures.Domain.Articles;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerArticleRepository(IOptions<DatabaseOptions> options) : IArticleRepository
{
    public async Task<IReadOnlyList<ArticleListItem>> GetArticlesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, a.code, a.label, af.code, af.label, ac.code, a.article_type, a.default_unit,
                   a.creation_mode, c.code, src.code, a.created_at,
                   a.configuration_session_id, a.duplication_session_id
            FROM articles a
            JOIN article_families af ON af.id = a.family_id
            JOIN article_categories ac ON ac.id = af.category_id
            LEFT JOIN customers c ON c.id = a.customer_id
            LEFT JOIN articles src ON src.id = a.source_article_id
            ORDER BY a.created_at DESC, a.code
            """;

        return await ReadArticleListAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<CategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, code, label FROM article_categories ORDER BY label";

        var items = new List<CategoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CategoryItem(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        return items;
    }

    public async Task<IReadOnlyList<FamilyItem>> GetFamiliesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT af.id, af.code, af.label, ac.code, ac.label
            FROM article_families af
            JOIN article_categories ac ON ac.id = af.category_id
            ORDER BY ac.label, af.label
            """;

        var items = new List<FamilyItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new FamilyItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return items;
    }

    public async Task<IReadOnlyList<CustomerItem>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, code, label FROM customers WHERE status = 'ACTIVE' ORDER BY label";

        var items = new List<CustomerItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CustomerItem(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        return items;
    }

    public async Task<IReadOnlyList<AttributeDefinitionItem>> GetAttributeDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, label, value_type, selection_mode, is_generator
            FROM attribute_definitions
            ORDER BY label
            """;

        var items = new List<AttributeDefinitionItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AttributeDefinitionItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5)));
        }

        return items;
    }

    public async Task<IReadOnlyList<AttributeOptionItem>> GetAttributeOptionsAsync(
        string? attributeCode = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ao.id, ao.attribute_id, ao.display_value, ao.normalized_value, ao.technical_code, ao.status
            FROM attribute_options ao
            JOIN attribute_definitions ad ON ad.id = ao.attribute_id
            WHERE (@attributeCode IS NULL OR ad.code = @attributeCode)
            ORDER BY ad.code, ao.display_value
            """;
        command.Parameters.AddWithValue("@attributeCode", (object?)attributeCode ?? DBNull.Value);

        var items = new List<AttributeOptionItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AttributeOptionItem(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return items;
    }

    public async Task<IReadOnlyList<AttributeFormattingRuleItem>> GetFormattingRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.attribute_id, ad.code, r.trim_spaces, r.collapse_spaces, r.remove_internal_spaces,
                   r.case_rule, r.strip_accents_for_code, r.forbidden_chars
            FROM attribute_formatting_rules r
            JOIN attribute_definitions ad ON ad.id = r.attribute_id
            ORDER BY ad.code
            """;

        var items = new List<AttributeFormattingRuleItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AttributeFormattingRuleItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetString(5),
                reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return items;
    }

    public async Task<IReadOnlyList<FamilyAttributeItem>> GetFamilyAttributesAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fa.attribute_id, ad.code, ad.label, fa.position, fa.is_visible, fa.is_required,
                   fa.is_generator, fa.allows_multi_select, fa.allows_create
            FROM article_family_attributes fa
            JOIN article_families af ON af.id = fa.family_id
            JOIN attribute_definitions ad ON ad.id = fa.attribute_id
            WHERE af.code = @familyCode
            ORDER BY fa.position
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var items = new List<FamilyAttributeItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new FamilyAttributeItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)));
        }

        return items;
    }

    public async Task<IReadOnlyList<CustomerMatrixAttributeItem>> GetCustomerMatrixAsync(
        string customerCode,
        string familyCode,
        string? seasonCode = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveMatrixAttributesAsync(customerCode, familyCode, seasonCode, cancellationToken);
        return resolved;
    }

    public async Task<FormatValueResult> FormatValueAsync(
        FormatValueRequest request,
        CancellationToken cancellationToken = default)
    {
        var rule = await LoadFormattingRuleAsync(request.AttributeCode, cancellationToken);
        var formatted = AttributeValueFormatter.Format(request.RawValue, rule);
        return new FormatValueResult(
            formatted.RawValue,
            formatted.DisplayValue,
            formatted.NormalizedValue,
            formatted.TechnicalCode);
    }

    public async Task<ArticleListItem> CreateManualArticleAsync(
        CreateManualArticleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new InvalidOperationException("Le code article est obligatoire.");
        }

        await using var connection = OpenConnection();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var familyId = await GetFamilyIdAsync(connection, transaction, request.FamilyCode, cancellationToken);
            await EnsureUniqueCodeAsync(connection, transaction, request.Code, cancellationToken);

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO articles (family_id, code, label, article_type, default_unit, creation_mode, customer_id)
                OUTPUT INSERTED.id
                VALUES (@familyId, @code, @label, @articleType, @unit, 'MANUAL', @customerId)
                """;
            insert.Parameters.AddWithValue("@familyId", familyId);
            insert.Parameters.AddWithValue("@code", request.Code.Trim());
            insert.Parameters.AddWithValue("@label", request.Label.Trim());
            insert.Parameters.AddWithValue("@articleType", request.ArticleType);
            insert.Parameters.AddWithValue("@unit", request.DefaultUnit);
            insert.Parameters.AddWithValue("@customerId", (object?)request.CustomerId ?? DBNull.Value);

            var articleId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);

            return await GetArticleByIdAsync(articleId, cancellationToken)
                ?? throw new InvalidOperationException("Article cree introuvable.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ConfigureArticlesResult> ConfigureArticlesAsync(
        ConfigureArticlesRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var customerId = await GetCustomerIdAsync(connection, transaction, request.CustomerCode, cancellationToken);
            var familyId = await GetFamilyIdAsync(connection, transaction, request.FamilyCode, cancellationToken);
            var configurationId = await GetConfigurationIdAsync(
                connection, transaction, customerId, familyId, null, cancellationToken);

            var matrix = await ResolveMatrixAttributesInternalAsync(
                connection, transaction, request.CustomerCode, request.FamilyCode, null, cancellationToken);

            var orderCode = request.CustomerOrderCode ?? "BC0001";
            var orderFormatted = await FormatInternalAsync(connection, transaction, "CUSTOMER_ORDER", orderCode, cancellationToken);
            var fgFormatted = await FormatInternalAsync(connection, transaction, "FINISHED_GOOD_CODE", request.FinishedGoodCode, cancellationToken);
            var compositionFormatted = await FormatInternalAsync(connection, transaction, "COMPOSITION", request.CompositionRaw, cancellationToken);
            var careFormatted = await FormatInternalAsync(connection, transaction, "CARE_CODE", request.CareCode, cancellationToken);

            var sizeCodes = new List<string>();
            foreach (var size in request.SizeValues)
            {
                var formatted = await FormatInternalAsync(connection, transaction, "SIZE", size, cancellationToken);
                sizeCodes.Add(formatted.TechnicalCode);
                await EnsureOptionAsync(connection, transaction, "SIZE", formatted, cancellationToken);
            }

            var colorCodes = new List<string>();
            foreach (var color in request.ColorValues)
            {
                var formatted = await FormatInternalAsync(connection, transaction, "COLOR", color, cancellationToken);
                colorCodes.Add(formatted.TechnicalCode);
                await EnsureOptionAsync(connection, transaction, "COLOR", formatted, cancellationToken);
            }

            var languageRule = matrix.FirstOrDefault(m => m.AttributeCode == "LANGUAGE");
            var languageIsGenerator = languageRule?.IsGenerator ?? false;
            var languageCodes = new List<string>();
            if (request.LanguageValues is not null)
            {
                foreach (var language in request.LanguageValues)
                {
                    var formatted = await FormatInternalAsync(connection, transaction, "LANGUAGE", language, cancellationToken);
                    languageCodes.Add(formatted.TechnicalCode);
                    await EnsureOptionAsync(connection, transaction, "LANGUAGE", formatted, cancellationToken);
                }
            }

            if (colorCodes.Count == 0)
            {
                var colorMatrix = matrix.FirstOrDefault(m => m.AttributeCode == "COLOR");
                if (colorMatrix is { IsRequired: true })
                {
                    throw new InvalidOperationException("Au moins une couleur est requise pour ce client.");
                }
            }

            var drafts = ArticleConfiguratorEngine.GenerateVignetteArticles(
                orderFormatted.TechnicalCode,
                fgFormatted.TechnicalCode,
                sizeCodes,
                colorCodes,
                languageCodes,
                languageIsGenerator);

            var bomLinkStatus = request.FamilyCode == "VIGNETTE_COMPOSITION" ? "A_CONFIRMER" : "NOT_LINKED";
            var sessionId = await CreateConfigurationSessionAsync(
                connection, transaction, customerId, familyId, configurationId, bomLinkStatus, cancellationToken);

            var createdArticles = new List<ArticleListItem>();
            foreach (var draft in drafts)
            {
                await EnsureUniqueCodeAsync(connection, transaction, draft.Code, cancellationToken);

                await using var insertArticle = connection.CreateCommand();
                insertArticle.Transaction = transaction;
                insertArticle.CommandText = """
                    INSERT INTO articles (family_id, code, label, article_type, default_unit, creation_mode,
                                          customer_id, configuration_session_id)
                    OUTPUT INSERTED.id
                    VALUES (@familyId, @code, @label, 'COMPONENT', 'PIECE', 'CONFIGURED', @customerId, @sessionId)
                    """;
                insertArticle.Parameters.AddWithValue("@familyId", familyId);
                insertArticle.Parameters.AddWithValue("@code", draft.Code);
                insertArticle.Parameters.AddWithValue("@label", draft.Label);
                insertArticle.Parameters.AddWithValue("@customerId", customerId);
                insertArticle.Parameters.AddWithValue("@sessionId", sessionId);
                var articleId = Convert.ToInt32(await insertArticle.ExecuteScalarAsync(cancellationToken));

                await InsertAttributeValueAsync(connection, transaction, articleId, "CUSTOMER_ORDER", orderFormatted, cancellationToken);
                await InsertAttributeValueAsync(connection, transaction, articleId, "FINISHED_GOOD_CODE", fgFormatted, cancellationToken);
                await InsertAttributeValueAsync(connection, transaction, articleId, "SIZE", await FormatInternalAsync(connection, transaction, "SIZE", draft.SizeTechnicalCode, cancellationToken), cancellationToken);
                await InsertAttributeValueAsync(connection, transaction, articleId, "COLOR", await FormatInternalAsync(connection, transaction, "COLOR", draft.ColorTechnicalCode, cancellationToken), cancellationToken);
                await InsertAttributeValueAsync(connection, transaction, articleId, "COMPOSITION", compositionFormatted, cancellationToken);
                await InsertAttributeValueAsync(connection, transaction, articleId, "CARE_CODE", careFormatted, cancellationToken);

                if (!string.IsNullOrWhiteSpace(draft.LanguageTechnicalCode))
                {
                    var langFormatted = await FormatInternalAsync(connection, transaction, "LANGUAGE", draft.LanguageTechnicalCode, cancellationToken);
                    await InsertAttributeValueAsync(connection, transaction, articleId, "LANGUAGE", langFormatted, cancellationToken);
                }

                await InsertConfigurationInputAsync(connection, transaction, sessionId, articleId, "CUSTOMER_ORDER", orderFormatted, cancellationToken);
                await InsertConfigurationInputAsync(connection, transaction, sessionId, articleId, "FINISHED_GOOD_CODE", fgFormatted, cancellationToken);
                await InsertConfigurationInputAsync(connection, transaction, sessionId, articleId, "SIZE", await FormatInternalAsync(connection, transaction, "SIZE", draft.SizeTechnicalCode, cancellationToken), cancellationToken);
                await InsertConfigurationInputAsync(connection, transaction, sessionId, articleId, "COLOR", await FormatInternalAsync(connection, transaction, "COLOR", draft.ColorTechnicalCode, cancellationToken), cancellationToken);

                var article = await GetArticleByIdInternalAsync(connection, transaction, articleId, cancellationToken);
                if (article is not null)
                {
                    createdArticles.Add(article);
                }
            }

            await transaction.CommitAsync(cancellationToken);

            return new ConfigureArticlesResult(
                sessionId,
                createdArticles.Count,
                createdArticles,
                bomLinkStatus,
                bomLinkStatus == "A_CONFIRMER"
                    ? "Lien BOM/gamme : À confirmer — recalcul nomenclature non implémenté dans ce MVP."
                    : null);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<DuplicateArticleResult> DuplicateArticleAsync(
        DuplicateArticleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewCode))
        {
            throw new InvalidOperationException("Un nouveau code article est obligatoire pour la duplication.");
        }

        await using var connection = OpenConnection();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var source = await GetArticleByIdInternalAsync(connection, transaction, request.SourceArticleId, cancellationToken)
                ?? throw new InvalidOperationException("Article source introuvable.");

            await EnsureUniqueCodeAsync(connection, transaction, request.NewCode, cancellationToken);

            await using var dupSession = connection.CreateCommand();
            dupSession.Transaction = transaction;
            dupSession.CommandText = """
                INSERT INTO article_duplication_sessions (source_article_id, new_article_code, status, copy_bom, copy_routing)
                OUTPUT INSERTED.id
                VALUES (@sourceId, @newCode, 'COMPLETED', @copyBom, @copyRouting)
                """;
            dupSession.Parameters.AddWithValue("@sourceId", request.SourceArticleId);
            dupSession.Parameters.AddWithValue("@newCode", request.NewCode.Trim());
            dupSession.Parameters.AddWithValue("@copyBom", request.CopyBom);
            dupSession.Parameters.AddWithValue("@copyRouting", request.CopyRouting);
            var sessionId = Convert.ToInt32(await dupSession.ExecuteScalarAsync(cancellationToken));

            await using var insertArticle = connection.CreateCommand();
            insertArticle.Transaction = transaction;
            insertArticle.CommandText = """
                INSERT INTO articles (family_id, code, label, article_type, default_unit, creation_mode,
                                      customer_id, source_article_id, duplication_session_id)
                OUTPUT INSERTED.id
                SELECT family_id, @newCode, @newLabel, article_type, default_unit, 'DUPLICATED',
                       customer_id, id, @sessionId
                FROM articles WHERE id = @sourceId
                """;
            insertArticle.Parameters.AddWithValue("@newCode", request.NewCode.Trim());
            insertArticle.Parameters.AddWithValue("@newLabel", request.NewLabel?.Trim() ?? $"{source.Label} (copie)");
            insertArticle.Parameters.AddWithValue("@sessionId", sessionId);
            insertArticle.Parameters.AddWithValue("@sourceId", request.SourceArticleId);
            var newArticleId = Convert.ToInt32(await insertArticle.ExecuteScalarAsync(cancellationToken));

            var changes = new List<DuplicationChangeItem>();
            await using var copyAttrs = connection.CreateCommand();
            copyAttrs.Transaction = transaction;
            copyAttrs.CommandText = """
                SELECT aav.attribute_id, ad.code, aav.display_value, aav.normalized_value, aav.option_id,
                       aav.raw_value, aav.technical_code
                FROM article_attribute_values aav
                JOIN attribute_definitions ad ON ad.id = aav.attribute_id
                WHERE aav.article_id = @sourceId
                """;
            copyAttrs.Parameters.AddWithValue("@sourceId", request.SourceArticleId);

            await using var reader = await copyAttrs.ExecuteReaderAsync(cancellationToken);
            var attributeRows = new List<(int AttributeId, string Code, string Display, string Normalized, int? OptionId, string? Raw, string Technical)>();
            while (await reader.ReadAsync(cancellationToken))
            {
                attributeRows.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6)));
            }

            await reader.CloseAsync();

            foreach (var row in attributeRows)
            {
                await using var insertAttr = connection.CreateCommand();
                insertAttr.Transaction = transaction;
                insertAttr.CommandText = """
                    INSERT INTO article_attribute_values
                        (article_id, attribute_id, option_id, raw_value, display_value, normalized_value, technical_code)
                    VALUES (@articleId, @attributeId, @optionId, @raw, @display, @normalized, @technical)
                    """;
                insertAttr.Parameters.AddWithValue("@articleId", newArticleId);
                insertAttr.Parameters.AddWithValue("@attributeId", row.AttributeId);
                insertAttr.Parameters.AddWithValue("@optionId", (object?)row.OptionId ?? DBNull.Value);
                insertAttr.Parameters.AddWithValue("@raw", (object?)row.Raw ?? DBNull.Value);
                insertAttr.Parameters.AddWithValue("@display", row.Display);
                insertAttr.Parameters.AddWithValue("@normalized", row.Normalized);
                insertAttr.Parameters.AddWithValue("@technical", row.Technical);
                await insertAttr.ExecuteNonQueryAsync(cancellationToken);

                await using var insertChange = connection.CreateCommand();
                insertChange.Transaction = transaction;
                insertChange.CommandText = """
                    INSERT INTO article_duplication_changes
                        (session_id, attribute_id, old_display_value, new_display_value, old_normalized_value, new_normalized_value)
                    VALUES (@sessionId, @attributeId, @oldDisplay, @newDisplay, @oldNorm, @newNorm)
                    """;
                insertChange.Parameters.AddWithValue("@sessionId", sessionId);
                insertChange.Parameters.AddWithValue("@attributeId", row.AttributeId);
                insertChange.Parameters.AddWithValue("@oldDisplay", row.Display);
                insertChange.Parameters.AddWithValue("@newDisplay", row.Display);
                insertChange.Parameters.AddWithValue("@oldNorm", row.Normalized);
                insertChange.Parameters.AddWithValue("@newNorm", row.Normalized);
                await insertChange.ExecuteNonQueryAsync(cancellationToken);

                changes.Add(new DuplicationChangeItem(row.Code, row.Display, row.Display));
            }

            await transaction.CommitAsync(cancellationToken);

            var article = await GetArticleByIdAsync(newArticleId, cancellationToken)
                ?? throw new InvalidOperationException("Article duplique introuvable.");

            string? bomNote = null;
            if (request.CopyBom || request.CopyRouting)
            {
                bomNote = "Copie BOM/gamme : À confirmer — non implémentée dans ce MVP.";
            }

            return new DuplicateArticleResult(sessionId, article, changes, bomNote);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CategoryItem> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO article_categories (code, label)
            OUTPUT INSERTED.id, INSERTED.code, INSERTED.label
            VALUES (@code, @label)
            """;
        command.Parameters.AddWithValue("@code", request.Code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@label", request.Label.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new CategoryItem(reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task<FamilyItem> CreateFamilyAsync(CreateFamilyRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO article_families (category_id, code, label)
            OUTPUT INSERTED.id, INSERTED.code, INSERTED.label
            SELECT ac.id, @code, @label
            FROM article_categories ac
            WHERE ac.code = @categoryCode
            """;
        command.Parameters.AddWithValue("@code", request.Code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@label", request.Label.Trim());
        command.Parameters.AddWithValue("@categoryCode", request.CategoryCode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Categorie introuvable : {request.CategoryCode}");
        }

        var family = new FamilyItem(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), request.CategoryCode, string.Empty);
        return family with { CategoryLabel = await GetCategoryLabelAsync(request.CategoryCode, cancellationToken) };
    }

    public async Task<AttributeDefinitionItem> CreateAttributeAsync(
        CreateAttributeRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO attribute_definitions (code, label, value_type, is_generator, selection_mode)
                OUTPUT INSERTED.id
                VALUES (@code, @label, @valueType, @isGenerator, @selectionMode)
                """;
            insert.Parameters.AddWithValue("@code", request.Code.Trim().ToUpperInvariant());
            insert.Parameters.AddWithValue("@label", request.Label.Trim());
            insert.Parameters.AddWithValue("@valueType", request.ValueType);
            insert.Parameters.AddWithValue("@isGenerator", request.IsGenerator);
            insert.Parameters.AddWithValue("@selectionMode", request.SelectionMode);
            var attributeId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));

            await using var rule = connection.CreateCommand();
            rule.Transaction = transaction;
            rule.CommandText = """
                INSERT INTO attribute_formatting_rules (attribute_id)
                VALUES (@attributeId)
                """;
            rule.Parameters.AddWithValue("@attributeId", attributeId);
            await rule.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new AttributeDefinitionItem(
                attributeId,
                request.Code.Trim().ToUpperInvariant(),
                request.Label.Trim(),
                request.ValueType,
                request.SelectionMode,
                request.IsGenerator);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AttributeOptionItem> CreateAttributeOptionAsync(
        CreateAttributeOptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var formatted = await FormatValueAsync(new FormatValueRequest(request.AttributeCode, request.RawValue), cancellationToken);

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
            OUTPUT INSERTED.id, INSERTED.attribute_id
            SELECT ad.id, @display, @normalized, @technical, @status
            FROM attribute_definitions ad
            WHERE ad.code = @attributeCode
            """;
        command.Parameters.AddWithValue("@display", formatted.DisplayValue);
        command.Parameters.AddWithValue("@normalized", formatted.NormalizedValue);
        command.Parameters.AddWithValue("@technical", formatted.TechnicalCode);
        command.Parameters.AddWithValue("@status", request.Status);
        command.Parameters.AddWithValue("@attributeCode", request.AttributeCode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Attribut introuvable : {request.AttributeCode}");
        }

        return new AttributeOptionItem(
            reader.GetInt32(0),
            reader.GetInt32(1),
            formatted.DisplayValue,
            formatted.NormalizedValue,
            formatted.TechnicalCode,
            request.Status);
    }

    public async Task<IReadOnlyList<TraceabilityItem>> GetTraceabilityAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var items = new List<TraceabilityItem>();

        await using var configCmd = connection.CreateCommand();
        configCmd.CommandText = """
            SELECT cs.id, cs.created_at, c.code, af.code, cs.status, cs.bom_link_status,
                   (SELECT COUNT(*) FROM articles a WHERE a.configuration_session_id = cs.id)
            FROM configuration_sessions cs
            LEFT JOIN customers c ON c.id = cs.customer_id
            JOIN article_families af ON af.id = cs.family_id
            ORDER BY cs.created_at DESC
            """;
        await using (var reader = await configCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new TraceabilityItem(
                    "CONFIGURATION",
                    reader.GetInt32(0),
                    reader.GetDateTime(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    null,
                    null,
                    reader.GetInt32(6),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        await using var dupCmd = connection.CreateCommand();
        dupCmd.CommandText = """
            SELECT ds.id, ds.created_at, src.code, ds.new_article_code, ds.status, ds.copy_bom, ds.copy_routing
            FROM article_duplication_sessions ds
            JOIN articles src ON src.id = ds.source_article_id
            ORDER BY ds.created_at DESC
            """;
        await using (var reader = await dupCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new TraceabilityItem(
                    "DUPLICATION",
                    reader.GetInt32(0),
                    reader.GetDateTime(1),
                    null,
                    null,
                    reader.GetString(2),
                    reader.GetString(3),
                    1,
                    reader.GetString(4),
                    reader.GetBoolean(5) || reader.GetBoolean(6) ? "BOM/gamme : À confirmer" : null));
            }
        }

        return items.OrderByDescending(i => i.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<BomLinkInfo>> GetBomLinksAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.code, pf.code, bb.code, rb.code, cs.bom_link_status
            FROM articles a
            LEFT JOIN configuration_sessions cs ON cs.id = a.configuration_session_id
            LEFT JOIN article_families af ON af.id = a.family_id
            LEFT JOIN product_families pf ON pf.article_family_id = af.id
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
            WHERE a.creation_mode IN ('CONFIGURED', 'DUPLICATED')
            ORDER BY a.code
            """;

        var items = new List<BomLinkInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new BomLinkInfo(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? "NOT_LINKED" : reader.GetString(4)));
        }

        return items;
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

    private async Task<AttributeFormattingRule> LoadFormattingRuleAsync(string attributeCode, CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.trim_spaces, r.collapse_spaces, r.remove_internal_spaces, r.case_rule,
                   r.strip_accents_for_code, r.forbidden_chars
            FROM attribute_formatting_rules r
            JOIN attribute_definitions ad ON ad.id = r.attribute_id
            WHERE ad.code = @code
            """;
        command.Parameters.AddWithValue("@code", attributeCode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AttributeFormattingRule();
        }

        return new AttributeFormattingRule(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private static async Task<FormattedValue> FormatInternalAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string attributeCode,
        string rawValue,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT r.trim_spaces, r.collapse_spaces, r.remove_internal_spaces, r.case_rule,
                   r.strip_accents_for_code, r.forbidden_chars
            FROM attribute_formatting_rules r
            JOIN attribute_definitions ad ON ad.id = r.attribute_id
            WHERE ad.code = @code
            """;
        command.Parameters.AddWithValue("@code", attributeCode);

        AttributeFormattingRule rule;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                rule = new AttributeFormattingRule(
                    reader.GetBoolean(0),
                    reader.GetBoolean(1),
                    reader.GetBoolean(2),
                    reader.GetString(3),
                    reader.GetBoolean(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5));
            }
            else
            {
                rule = new AttributeFormattingRule();
            }
        }

        return AttributeValueFormatter.Format(rawValue, rule);
    }

    private static async Task EnsureOptionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string attributeCode,
        FormattedValue formatted,
        CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = """
            SELECT ao.id FROM attribute_options ao
            JOIN attribute_definitions ad ON ad.id = ao.attribute_id
            WHERE ad.code = @code AND ao.normalized_value = @normalized
            """;
        check.Parameters.AddWithValue("@code", attributeCode);
        check.Parameters.AddWithValue("@normalized", formatted.NormalizedValue);
        var existing = await check.ExecuteScalarAsync(cancellationToken);
        if (existing is not null)
        {
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
            SELECT ad.id, @display, @normalized, @technical, 'TO_VALIDATE'
            FROM attribute_definitions ad WHERE ad.code = @code
            """;
        insert.Parameters.AddWithValue("@display", formatted.DisplayValue);
        insert.Parameters.AddWithValue("@normalized", formatted.NormalizedValue);
        insert.Parameters.AddWithValue("@technical", formatted.TechnicalCode);
        insert.Parameters.AddWithValue("@code", attributeCode);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAttributeValueAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int articleId,
        string attributeCode,
        FormattedValue formatted,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO article_attribute_values
                (article_id, attribute_id, raw_value, display_value, normalized_value, technical_code)
            SELECT @articleId, ad.id, @raw, @display, @normalized, @technical
            FROM attribute_definitions ad WHERE ad.code = @code
            """;
        command.Parameters.AddWithValue("@articleId", articleId);
        command.Parameters.AddWithValue("@raw", formatted.RawValue);
        command.Parameters.AddWithValue("@display", formatted.DisplayValue);
        command.Parameters.AddWithValue("@normalized", formatted.NormalizedValue);
        command.Parameters.AddWithValue("@technical", formatted.TechnicalCode);
        command.Parameters.AddWithValue("@code", attributeCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertConfigurationInputAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int sessionId,
        int articleId,
        string attributeCode,
        FormattedValue formatted,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO configuration_inputs
                (session_id, attribute_id, raw_value, display_value, normalized_value, technical_code, generated_article_id)
            SELECT @sessionId, ad.id, @raw, @display, @normalized, @technical, @articleId
            FROM attribute_definitions ad WHERE ad.code = @code
            """;
        command.Parameters.AddWithValue("@sessionId", sessionId);
        command.Parameters.AddWithValue("@articleId", articleId);
        command.Parameters.AddWithValue("@raw", formatted.RawValue);
        command.Parameters.AddWithValue("@display", formatted.DisplayValue);
        command.Parameters.AddWithValue("@normalized", formatted.NormalizedValue);
        command.Parameters.AddWithValue("@technical", formatted.TechnicalCode);
        command.Parameters.AddWithValue("@code", attributeCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CreateConfigurationSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int customerId,
        int familyId,
        int? configurationId,
        string bomLinkStatus,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO configuration_sessions (customer_id, family_id, configuration_id, status, bom_link_status)
            OUTPUT INSERTED.id
            VALUES (@customerId, @familyId, @configurationId, 'COMPLETED', @bomLinkStatus)
            """;
        command.Parameters.AddWithValue("@customerId", customerId);
        command.Parameters.AddWithValue("@familyId", familyId);
        command.Parameters.AddWithValue("@configurationId", (object?)configurationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@bomLinkStatus", bomLinkStatus);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task EnsureUniqueCodeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string code,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM articles WHERE code = @code";
        command.Parameters.AddWithValue("@code", code.Trim());
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (count > 0)
        {
            throw new InvalidOperationException($"Le code article '{code}' existe deja.");
        }
    }

    private static async Task<int> GetFamilyIdAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = "SELECT id FROM article_families WHERE code = @code";
        command.Parameters.AddWithValue("@code", familyCode);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int id
            ? id
            : throw new InvalidOperationException($"Famille introuvable : {familyCode}");
    }

    private static async Task<int> GetCustomerIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string customerCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM customers WHERE code = @code";
        command.Parameters.AddWithValue("@code", customerCode);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int id
            ? id
            : throw new InvalidOperationException($"Client introuvable : {customerCode}");
    }

    private static async Task<int?> GetConfigurationIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int customerId,
        int familyId,
        string? seasonCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id FROM customer_article_family_configurations
            WHERE customer_id = @customerId AND family_id = @familyId
              AND ((season_code IS NULL AND @seasonCode IS NULL) OR season_code = @seasonCode)
            """;
        command.Parameters.AddWithValue("@customerId", customerId);
        command.Parameters.AddWithValue("@familyId", familyId);
        command.Parameters.AddWithValue("@seasonCode", (object?)seasonCode ?? DBNull.Value);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int id ? id : null;
    }

    private async Task<IReadOnlyList<CustomerMatrixAttributeItem>> ResolveMatrixAttributesAsync(
        string customerCode,
        string familyCode,
        string? seasonCode,
        CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        return await ResolveMatrixAttributesInternalAsync(connection, null, customerCode, familyCode, seasonCode, cancellationToken);
    }

    private static async Task<IReadOnlyList<CustomerMatrixAttributeItem>> ResolveMatrixAttributesInternalAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string customerCode,
        string familyCode,
        string? seasonCode,
        CancellationToken cancellationToken)
    {
        var customerRules = await LoadCustomerMatrixRulesAsync(connection, transaction, customerCode, familyCode, seasonCode, cancellationToken);
        if (customerRules.Count > 0)
        {
            return customerRules;
        }

        return await LoadFamilyDefaultRulesAsync(connection, transaction, familyCode, cancellationToken);
    }

    private static async Task<List<CustomerMatrixAttributeItem>> LoadCustomerMatrixRulesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string customerCode,
        string familyCode,
        string? seasonCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = """
            SELECT ca.attribute_id, ad.code, ad.label, ca.is_visible, ca.is_required, ca.is_modifiable,
                   ca.allows_multi_select, ca.allows_create, ca.is_generator, ca.role_in_code,
                   ca.role_in_description, ca.role_in_bom, ca.role_in_pegging, ca.validation_status
            FROM customer_article_family_attributes ca
            JOIN customer_article_family_configurations cfg ON cfg.id = ca.configuration_id
            JOIN customers c ON c.id = cfg.customer_id
            JOIN article_families af ON af.id = cfg.family_id
            JOIN attribute_definitions ad ON ad.id = ca.attribute_id
            WHERE c.code = @customerCode AND af.code = @familyCode
              AND ((cfg.season_code IS NULL AND @seasonCode IS NULL) OR cfg.season_code = @seasonCode)
            ORDER BY ad.code
            """;
        command.Parameters.AddWithValue("@customerCode", customerCode);
        command.Parameters.AddWithValue("@familyCode", familyCode);
        command.Parameters.AddWithValue("@seasonCode", (object?)seasonCode ?? DBNull.Value);

        var items = new List<CustomerMatrixAttributeItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CustomerMatrixAttributeItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetBoolean(11),
                reader.GetBoolean(12),
                reader.GetString(13),
                "CLIENT"));
        }

        return items;
    }

    private static async Task<List<CustomerMatrixAttributeItem>> LoadFamilyDefaultRulesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = """
            SELECT fa.attribute_id, ad.code, ad.label, fa.is_visible, fa.is_required, 1,
                   fa.allows_multi_select, fa.allows_create, fa.is_generator, 0, 1, 0, 0, 'VALIDATED'
            FROM article_family_attributes fa
            JOIN article_families af ON af.id = fa.family_id
            JOIN attribute_definitions ad ON ad.id = fa.attribute_id
            WHERE af.code = @familyCode
            ORDER BY fa.position
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var items = new List<CustomerMatrixAttributeItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CustomerMatrixAttributeItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetBoolean(11),
                reader.GetBoolean(12),
                reader.GetString(13),
                "FAMILLE"));
        }

        return items;
    }

    private async Task<ArticleListItem?> GetArticleByIdAsync(int articleId, CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        return await GetArticleByIdInternalAsync(connection, null, articleId, cancellationToken);
    }

    private static async Task<ArticleListItem?> GetArticleByIdInternalAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int articleId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = """
            SELECT a.id, a.code, a.label, af.code, af.label, ac.code, a.article_type, a.default_unit,
                   a.creation_mode, c.code, src.code, a.created_at,
                   a.configuration_session_id, a.duplication_session_id
            FROM articles a
            JOIN article_families af ON af.id = a.family_id
            JOIN article_categories ac ON ac.id = af.category_id
            LEFT JOIN customers c ON c.id = a.customer_id
            LEFT JOIN articles src ON src.id = a.source_article_id
            WHERE a.id = @id
            """;
        command.Parameters.AddWithValue("@id", articleId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapArticle(reader);
    }

    private static async Task<List<ArticleListItem>> ReadArticleListAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var items = new List<ArticleListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapArticle(reader));
        }

        return items;
    }

    private static ArticleListItem MapArticle(SqlDataReader reader)
    {
        return new ArticleListItem(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetDateTime(11),
            reader.IsDBNull(12) ? null : reader.GetInt32(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13));
    }

    private async Task<string> GetCategoryLabelAsync(string categoryCode, CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT label FROM article_categories WHERE code = @code";
        command.Parameters.AddWithValue("@code", categoryCode);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string ?? categoryCode;
    }
}
