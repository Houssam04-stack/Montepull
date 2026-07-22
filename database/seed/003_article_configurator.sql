-- Seed Configurateur Article (cadrage V0.1)

INSERT INTO customers (code, label) VALUES
('CLIENT_A', 'Client A'),
('CLIENT_B', 'Client B');

INSERT INTO attribute_definitions (code, label, value_type, is_generator, selection_mode) VALUES
('FINISHED_GOOD_CODE', 'Code article produit fini', 'TEXT', 1, 'CONTROLLED'),
('CARE_CODE', 'Code entretien', 'OPTION', 0, 'CONTROLLED'),
('LANGUAGE', 'Langue', 'OPTION', 1, 'CONTROLLED_WITH_CREATE'),
('DESTINATION_COUNTRY', 'Pays de destination', 'OPTION', 0, 'CONTROLLED');

UPDATE attribute_definitions SET selection_mode = 'CONTROLLED_WITH_CREATE' WHERE code IN ('SIZE', 'COLOR', 'COMPOSITION');
UPDATE attribute_definitions SET selection_mode = 'CONTROLLED' WHERE code = 'CUSTOMER_ORDER';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
SELECT id, 'BC0001', 'BC0001', 'BC0001', 'VALIDATED' FROM attribute_definitions WHERE code = 'CUSTOMER_ORDER';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
SELECT id, 'LAV30-SECHNON', 'LAV30-SECHNON', 'LAV30-SECHNON', 'VALIDATED' FROM attribute_definitions WHERE code = 'CARE_CODE';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
SELECT id, 'FR', 'FR', 'FR', 'VALIDATED' FROM attribute_definitions WHERE code = 'LANGUAGE';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
SELECT id, 'EN', 'EN', 'EN', 'VALIDATED' FROM attribute_definitions WHERE code = 'LANGUAGE';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
SELECT id, 'XXL', 'XXL', 'XXL', 'VALIDATED' FROM attribute_definitions WHERE code = 'SIZE';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
SELECT id, 'Bleu marine', 'BLEU MARINE', 'BLEU-MARINE', 'VALIDATED' FROM attribute_definitions WHERE code = 'COLOR';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
SELECT id, 'Vert sauge', 'VERT SAUGE', 'VERT-SAUGE', 'VALIDATED' FROM attribute_definitions WHERE code = 'COLOR';

INSERT INTO attribute_formatting_rules (attribute_id, trim_spaces, collapse_spaces, remove_internal_spaces, case_rule, strip_accents_for_code, forbidden_chars)
SELECT id, 1, 1, 1, 'UPPER', 1, NULL FROM attribute_definitions WHERE code = 'SIZE';

INSERT INTO attribute_formatting_rules (attribute_id, trim_spaces, collapse_spaces, remove_internal_spaces, case_rule, strip_accents_for_code, forbidden_chars)
SELECT id, 1, 1, 0, 'TITLE', 1, NULL FROM attribute_definitions WHERE code = 'COLOR';

INSERT INTO attribute_formatting_rules (attribute_id, trim_spaces, collapse_spaces, remove_internal_spaces, case_rule, strip_accents_for_code, forbidden_chars)
SELECT id, 1, 1, 1, 'UPPER', 1, NULL FROM attribute_definitions WHERE code = 'CUSTOMER_ORDER';

INSERT INTO attribute_formatting_rules (attribute_id, trim_spaces, collapse_spaces, remove_internal_spaces, case_rule, strip_accents_for_code, forbidden_chars)
SELECT id, 1, 1, 0, 'NONE', 0, NULL FROM attribute_definitions WHERE code = 'COMPOSITION';

INSERT INTO attribute_formatting_rules (attribute_id, trim_spaces, collapse_spaces, remove_internal_spaces, case_rule, strip_accents_for_code, forbidden_chars)
SELECT id, 1, 1, 1, 'UPPER', 1, NULL FROM attribute_definitions WHERE code IN ('CARE_CODE', 'LANGUAGE', 'DESTINATION_COUNTRY', 'FINISHED_GOOD_CODE');

-- Famille vignette : attributs par defaut
INSERT INTO article_family_attributes (family_id, attribute_id, position, is_visible, is_required, is_generator, allows_multi_select, allows_create)
SELECT af.id, ad.id, 10, 1, 1, 1, 0, 0
FROM article_families af CROSS JOIN attribute_definitions ad
WHERE af.code = 'VIGNETTE_COMPOSITION' AND ad.code = 'CUSTOMER_ORDER';

INSERT INTO article_family_attributes (family_id, attribute_id, position, is_visible, is_required, is_generator, allows_multi_select, allows_create)
SELECT af.id, ad.id, 20, 1, 1, 1, 0, 0
FROM article_families af CROSS JOIN attribute_definitions ad
WHERE af.code = 'VIGNETTE_COMPOSITION' AND ad.code = 'FINISHED_GOOD_CODE';

INSERT INTO article_family_attributes (family_id, attribute_id, position, is_visible, is_required, is_generator, allows_multi_select, allows_create)
SELECT af.id, ad.id, 30, 1, 1, 1, 1, 1
FROM article_families af CROSS JOIN attribute_definitions ad
WHERE af.code = 'VIGNETTE_COMPOSITION' AND ad.code = 'SIZE';

INSERT INTO article_family_attributes (family_id, attribute_id, position, is_visible, is_required, is_generator, allows_multi_select, allows_create)
SELECT af.id, ad.id, 40, 1, 1, 1, 1, 1
FROM article_families af CROSS JOIN attribute_definitions ad
WHERE af.code = 'VIGNETTE_COMPOSITION' AND ad.code = 'COLOR';

INSERT INTO article_family_attributes (family_id, attribute_id, position, is_visible, is_required, is_generator, allows_multi_select, allows_create)
SELECT af.id, ad.id, 50, 1, 1, 0, 0, 1
FROM article_families af CROSS JOIN attribute_definitions ad
WHERE af.code = 'VIGNETTE_COMPOSITION' AND ad.code = 'COMPOSITION';

INSERT INTO article_family_attributes (family_id, attribute_id, position, is_visible, is_required, is_generator, allows_multi_select, allows_create)
SELECT af.id, ad.id, 60, 1, 1, 0, 0, 0
FROM article_families af CROSS JOIN attribute_definitions ad
WHERE af.code = 'VIGNETTE_COMPOSITION' AND ad.code = 'CARE_CODE';

INSERT INTO article_family_attributes (family_id, attribute_id, position, is_visible, is_required, is_generator, allows_multi_select, allows_create)
SELECT af.id, ad.id, 70, 1, 0, 0, 0, 0
FROM article_families af CROSS JOIN attribute_definitions ad
WHERE af.code = 'VIGNETTE_COMPOSITION' AND ad.code = 'LANGUAGE';

-- Matrice client A / vignette composition
INSERT INTO customer_article_family_configurations (customer_id, family_id, season_code, status)
SELECT c.id, af.id, NULL, 'ACTIVE'
FROM customers c CROSS JOIN article_families af
WHERE c.code = 'CLIENT_A' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_configurations (customer_id, family_id, season_code, status)
SELECT c.id, af.id, NULL, 'ACTIVE'
FROM customers c CROSS JOIN article_families af
WHERE c.code = 'CLIENT_B' AND af.code = 'VIGNETTE_COMPOSITION';

-- Client A : couleur obligatoire generatrice, langue optionnelle non generatrice
INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, allows_multi_select, allows_create, is_generator, role_in_code, role_in_description, role_in_pegging)
SELECT cfg.id, ad.id, 1, 1, 0, 0, 1, 1, 1, 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'CUSTOMER_ORDER'
WHERE c.code = 'CLIENT_A' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, allows_multi_select, allows_create, is_generator, role_in_code, role_in_description, role_in_pegging)
SELECT cfg.id, ad.id, 1, 1, 0, 0, 1, 1, 1, 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'FINISHED_GOOD_CODE'
WHERE c.code = 'CLIENT_A' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, allows_multi_select, allows_create, is_generator, role_in_code)
SELECT cfg.id, ad.id, 1, 1, 1, 1, 1, 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'SIZE'
WHERE c.code = 'CLIENT_A' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, allows_multi_select, allows_create, is_generator, role_in_code)
SELECT cfg.id, ad.id, 1, 1, 1, 1, 1, 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'COLOR'
WHERE c.code = 'CLIENT_A' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, allows_create, validation_status, role_in_description)
SELECT cfg.id, ad.id, 1, 1, 1, 'TO_VALIDATE', 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'COMPOSITION'
WHERE c.code = 'CLIENT_A' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, role_in_code)
SELECT cfg.id, ad.id, 1, 1, 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'CARE_CODE'
WHERE c.code = 'CLIENT_A' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, is_generator)
SELECT cfg.id, ad.id, 1, 0, 0
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'LANGUAGE'
WHERE c.code = 'CLIENT_A' AND af.code = 'VIGNETTE_COMPOSITION';

-- Client B : couleur optionnelle generatrice, langue obligatoire generatrice, pays destination obligatoire
INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, allows_multi_select, allows_create, is_generator, role_in_code, role_in_description, role_in_pegging)
SELECT cfg.id, ad.id, 1, 1, 0, 0, 1, 0, 1, 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code IN ('CUSTOMER_ORDER', 'FINISHED_GOOD_CODE', 'SIZE', 'COMPOSITION', 'CARE_CODE')
WHERE c.code = 'CLIENT_B' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, allows_multi_select, allows_create, is_generator, role_in_code)
SELECT cfg.id, ad.id, 1, 0, 1, 1, 1, 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'COLOR'
WHERE c.code = 'CLIENT_B' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, allows_multi_select, is_generator, role_in_code)
SELECT cfg.id, ad.id, 1, 1, 1, 1, 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'LANGUAGE'
WHERE c.code = 'CLIENT_B' AND af.code = 'VIGNETTE_COMPOSITION';

INSERT INTO customer_article_family_attributes (configuration_id, attribute_id, is_visible, is_required, role_in_description)
SELECT cfg.id, ad.id, 1, 1, 1
FROM customer_article_family_configurations cfg
JOIN customers c ON c.id = cfg.customer_id
JOIN article_families af ON af.id = cfg.family_id
JOIN attribute_definitions ad ON ad.code = 'DESTINATION_COUNTRY'
WHERE c.code = 'CLIENT_B' AND af.code = 'VIGNETTE_COMPOSITION';
