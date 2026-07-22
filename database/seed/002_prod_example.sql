INSERT INTO routing_bases (product_family_id, code, version, status)
SELECT id, 'GAM_BASE_PULL_COL_ROND_PROD_EXEMPLE', 1, 'DRAFT'
FROM product_families
WHERE code = 'PULL_COL_ROND';

-- Donnees confirmees depuis le fichier prod :
-- operations, workcenters, quantite visible et temps visible.
-- L'unite exacte du temps reste a confirmer, donc time_unit = TO_CONFIRM.
INSERT INTO routing_base_operations (routing_base_id, operation_no, name, workcenter_id, quantity_base, time_base, time_unit, behavior)
SELECT rb.id, v.operation_no, v.operation_name, wc.id, 30, v.time_base, 'TO_CONFIRM', 'COPY_TO_VALIDATE'
FROM routing_bases rb
CROSS JOIN (
    SELECT 10 AS operation_no, 'Tricotage' AS operation_name, 'WU-PMA25' AS workcenter_code, 15.00 AS time_base UNION ALL
    SELECT 20, 'Mise en paquet', 'WU-PMA25', 30.00 UNION ALL
    SELECT 30, 'Bordage panneaux', 'WU-PO21', 24.00 UNION ALL
    SELECT 40, 'Lavage', 'WU-PO21', 21.00 UNION ALL
    SELECT 50, 'Sechoire', 'WU-PO21', 18.00 UNION ALL
    SELECT 60, 'Controle panneaux', 'WU-PO21', 15.00 UNION ALL
    SELECT 70, 'Calandrage', 'WU-PMA33', 15.00 UNION ALL
    SELECT 80, 'Matlassage', 'WU-PMA16', 60.00 UNION ALL
    SELECT 90, 'Coupe scie', 'WU-PMA16', 18.00 UNION ALL
    SELECT 100, 'Compositage', 'WU-PMA01', 15.00 UNION ALL
    SELECT 110, 'Montage epaule', 'WU-PMA01', 18.00 UNION ALL
    SELECT 120, 'Montage emmanchure', 'WU-PMA01', 45.00 UNION ALL
    SELECT 130, 'Montage cote', 'WU-PMA01', 60.00 UNION ALL
    SELECT 140, 'Coupe col', 'WU-PMA01', 30.00 UNION ALL
    SELECT 150, 'Bordage encolure', 'WU-PMA01', 18.00 UNION ALL
    SELECT 160, 'Montage bande col REM', 'WU-PMA01', 90.00 UNION ALL
    SELECT 170, 'Separation et demaillage bande col', 'WU-PMA01', 30.00 UNION ALL
    SELECT 180, 'Fermeture epaule', 'WU-PMA01', 15.00 UNION ALL
    SELECT 190, 'Point d''arret bas cote', 'WU-PMA01', 9.00 UNION ALL
    SELECT 200, 'Point d''arret bas manche', 'WU-PMA01', 9.00 UNION ALL
    SELECT 210, 'Point d''arret col', 'WU-PMA01', 6.00 UNION ALL
    SELECT 220, 'Fixation griffe de marque', 'WU-PMA01', 15.00 UNION ALL
    SELECT 230, 'Fixation V de composition', 'WU-PMA01', 9.00 UNION ALL
    SELECT 240, 'Finition', 'WU-PMA01', 60.00 UNION ALL
    SELECT 250, 'Controle', 'WU-PMA01', 60.00 UNION ALL
    SELECT 260, 'Repassage', 'WU-PMA38', 60.00 UNION ALL
    SELECT 270, 'Controle mesure', 'WU-PMA41', 24.00 UNION ALL
    SELECT 280, 'Controle final', 'WU-PMA41', 24.00 UNION ALL
    SELECT 290, 'Etiquetage', 'WU-PMA41', 18.00 UNION ALL
    SELECT 300, 'Mise en sachet', 'WU-PMA41', 15.00
) AS v
JOIN workcenters wc ON wc.code = v.workcenter_code
WHERE rb.code = 'GAM_BASE_PULL_COL_ROND_PROD_EXEMPLE';

INSERT INTO routing_operation_generation_rules (routing_base_operation_id, rule_code, behavior, missing_rule_strategy, status, notes)
SELECT id, 'ROUTING_COPY_TO_VALIDATE_MVP', 'COPY_TO_VALIDATE', 'COPY_BASE', 'VALIDATED',
       'Regle MVP simulee : les operations prod sont copiees et restent a valider metier.'
FROM routing_base_operations;

INSERT INTO bom_bases (product_family_id, code, version, status)
SELECT id, 'BOM_BASE_PULL_COL_ROND_EXEMPLE', 1, 'DRAFT'
FROM product_families
WHERE code = 'PULL_COL_ROND';

-- Donnees simulees pour rendre le MVP executable localement.
-- FIL-MINT, VCOMP-STD et SACHET-STD ne sont pas confirmes par le fichier prod.
INSERT INTO bom_base_lines (bom_base_id, line_no, component_article_id, quantity_base, unit, loss_rate, behavior)
SELECT b.id, 10, a.id, 0.500, 'KG', 0.05, 'CALCULATED'
FROM bom_bases b
JOIN articles a ON a.code = 'FIL-MINT'
WHERE b.code = 'BOM_BASE_PULL_COL_ROND_EXEMPLE';

INSERT INTO bom_base_lines (bom_base_id, line_no, component_article_id, quantity_base, unit, loss_rate, behavior)
SELECT b.id, 20, a.id, 1.000, 'PIECE', 0.00, 'FIXED'
FROM bom_bases b
JOIN articles a ON a.code = 'VCOMP-STD'
WHERE b.code = 'BOM_BASE_PULL_COL_ROND_EXEMPLE';

INSERT INTO bom_base_lines (bom_base_id, line_no, component_article_id, quantity_base, unit, loss_rate, behavior)
SELECT b.id, 30, a.id, 1.000, 'PIECE', 0.00, 'FIXED'
FROM bom_bases b
JOIN articles a ON a.code = 'SACHET-STD'
WHERE b.code = 'BOM_BASE_PULL_COL_ROND_EXEMPLE';

INSERT INTO bom_line_generation_rules (bom_base_line_id, rule_code, behavior, missing_rule_strategy, status, notes)
SELECT id, 'BOM_LINE_MVP_' + CAST(line_no AS NVARCHAR(20)), behavior, 'COPY_BASE', 'VALIDATED',
       'Regle MVP simulee pour tests locaux, a remplacer par regles metier confirmees.'
FROM bom_base_lines;

INSERT INTO generation_profiles (product_family_id, code, label, status, can_generate_bom, can_generate_routing)
SELECT id, 'PROFILE_SIZE_COLOR', 'Generation taille x couleur', 'VALIDATED', 1, 1
FROM product_families
WHERE code = 'PULL_COL_ROND';

INSERT INTO generation_profile_dimensions (profile_id, attribute_id, position, is_required)
SELECT gp.id, ad.id, 1, 1
FROM generation_profiles gp
JOIN attribute_definitions ad ON ad.code = 'SIZE'
WHERE gp.code = 'PROFILE_SIZE_COLOR';

INSERT INTO generation_profile_dimensions (profile_id, attribute_id, position, is_required)
SELECT gp.id, ad.id, 2, 1
FROM generation_profiles gp
JOIN attribute_definitions ad ON ad.code = 'COLOR'
WHERE gp.code = 'PROFILE_SIZE_COLOR';

INSERT INTO consumption_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
SELECT pf.id, ad.id, ao.id,
       CASE ao.technical_code WHEN 'L' THEN 1.10 ELSE 1.00 END,
       'SIMULATED', 'VALIDATED',
       'Coefficient simule pour valider la formule MVP.'
FROM product_families pf
JOIN attribute_definitions ad ON ad.code = 'SIZE'
JOIN attribute_options ao ON ao.attribute_id = ad.id
WHERE pf.code = 'PULL_COL_ROND';

INSERT INTO consumption_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
SELECT pf.id, ad.id, ao.id,
       CASE ao.technical_code WHEN 'NOIR' THEN 1.02 ELSE 1.00 END,
       'SIMULATED', 'VALIDATED',
       'Coefficient simule pour valider la formule MVP.'
FROM product_families pf
JOIN attribute_definitions ad ON ad.code = 'COLOR'
JOIN attribute_options ao ON ao.attribute_id = ad.id
WHERE pf.code = 'PULL_COL_ROND';

INSERT INTO time_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
SELECT pf.id, ad.id, ao.id,
       CASE ao.technical_code WHEN 'L' THEN 1.10 ELSE 1.00 END,
       'SIMULATED', 'VALIDATED',
       'Coefficient de temps simule pour valider la formule MVP.'
FROM product_families pf
JOIN attribute_definitions ad ON ad.code = 'SIZE'
JOIN attribute_options ao ON ao.attribute_id = ad.id
WHERE pf.code = 'PULL_COL_ROND';

INSERT INTO time_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
SELECT pf.id, ad.id, ao.id,
       1.00,
       'SIMULATED', 'VALIDATED',
       'Coefficient de temps simule pour valider la formule MVP.'
FROM product_families pf
JOIN attribute_definitions ad ON ad.code = 'COLOR'
JOIN attribute_options ao ON ao.attribute_id = ad.id
WHERE pf.code = 'PULL_COL_ROND';
