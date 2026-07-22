-- Arguments de formule et formule de besoin par defaut
UPDATE attribute_definitions
SET is_formula_argument = 1, is_active = 1
WHERE code IN ('SIZE', 'COLOR');

IF NOT EXISTS (SELECT 1 FROM attribute_definitions WHERE code = 'GAUGE')
BEGIN
    INSERT INTO attribute_definitions (code, label, value_type, is_generator, is_formula_argument, is_active, selection_mode)
    VALUES ('GAUGE', 'Jauge', 'OPTION', 0, 1, 1, 'CONTROLLED');
END
ELSE
BEGIN
    UPDATE attribute_definitions
    SET is_formula_argument = 1, is_active = 1, label = 'Jauge'
    WHERE code = 'GAUGE';
END;

IF NOT EXISTS (SELECT 1 FROM attribute_options ao JOIN attribute_definitions ad ON ad.id = ao.attribute_id WHERE ad.code = 'GAUGE' AND ao.technical_code = '3')
    INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
    SELECT id, '3', '3', '3', 'VALIDATED' FROM attribute_definitions WHERE code = 'GAUGE';

IF NOT EXISTS (SELECT 1 FROM attribute_options ao JOIN attribute_definitions ad ON ad.id = ao.attribute_id WHERE ad.code = 'GAUGE' AND ao.technical_code = '7')
    INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
    SELECT id, '7', '7', '7', 'VALIDATED' FROM attribute_definitions WHERE code = 'GAUGE';

IF NOT EXISTS (SELECT 1 FROM attribute_options ao JOIN attribute_definitions ad ON ad.id = ao.attribute_id WHERE ad.code = 'GAUGE' AND ao.technical_code = '12')
    INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
    SELECT id, '12', '12', '12', 'VALIDATED' FROM attribute_definitions WHERE code = 'GAUGE';

INSERT INTO consumption_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
SELECT pf.id, ad.id, ao.id, 1.0, 'CONFIRMED', 'VALIDATED', 'Defaut formule'
FROM product_families pf
CROSS JOIN attribute_definitions ad
JOIN attribute_options ao ON ao.attribute_id = ad.id
WHERE ad.code = 'GAUGE'
  AND NOT EXISTS (
      SELECT 1
      FROM consumption_coefficients cc
      WHERE cc.product_family_id = pf.id
        AND cc.attribute_id = ad.id
        AND cc.option_id = ao.id
  );

-- Coefficients manquants pour options ajoutees apres le seed 002 (XXL, Bleu marine, Vert sauge, etc.)
INSERT INTO consumption_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
SELECT pf.id, ad.id, ao.id, 1.0, 'CONFIRMED', 'VALIDATED', 'Coefficient auto argument formule'
FROM product_families pf
CROSS JOIN attribute_definitions ad
JOIN attribute_options ao ON ao.attribute_id = ad.id
WHERE ad.code IN ('SIZE', 'COLOR')
  AND ao.status <> 'OBSOLETE'
  AND NOT EXISTS (
      SELECT 1
      FROM consumption_coefficients cc
      WHERE cc.product_family_id = pf.id
        AND cc.attribute_id = ad.id
        AND cc.option_id = ao.id
  );

INSERT INTO requirement_formulas (product_family_id, target, expression, display_expression, apply_order_quantity, status)
SELECT pf.id, 'REQUIREMENT', 'BesoinBase * SIZE * COLOR * GAUGE', 'Besoin de base * Taille * Couleur * Jauge', 1, 'VALIDATED'
FROM product_families pf
WHERE NOT EXISTS (
    SELECT 1
    FROM requirement_formulas rf
    WHERE rf.product_family_id = pf.id
      AND rf.target = 'REQUIREMENT'
);
