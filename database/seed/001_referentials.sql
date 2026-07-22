INSERT INTO article_categories (code, label) VALUES
('FINISHED_GOOD', 'Produit fini'),
('SEMI_FINISHED', 'Semi-fini'),
('MAIN_MATERIAL', 'Matiere principale'),
('LABELING', 'Etiquetage'),
('PACKAGING', 'Packaging'),
('SERVICE', 'Service');

INSERT INTO article_families (category_id, code, label)
SELECT id, 'PULL', 'Pull' FROM article_categories WHERE code = 'FINISHED_GOOD';

INSERT INTO article_families (category_id, code, label)
SELECT id, 'FIL', 'Fil' FROM article_categories WHERE code = 'MAIN_MATERIAL';

INSERT INTO article_families (category_id, code, label)
SELECT id, 'VIGNETTE_COMPOSITION', 'Vignette composition' FROM article_categories WHERE code = 'LABELING';

INSERT INTO article_families (category_id, code, label)
SELECT id, 'PACKAGING_SACHET', 'Sachet' FROM article_categories WHERE code = 'PACKAGING';

INSERT INTO attribute_definitions (code, label, value_type, is_generator, is_formula_argument, is_active) VALUES
('SIZE', 'Taille', 'OPTION', 1, 1, 1),
('COLOR', 'Couleur', 'OPTION', 1, 1, 1),
('CUSTOMER_ORDER', 'Commande client', 'TEXT', 1, 0, 1),
('COMPOSITION', 'Composition matiere', 'TEXT', 1, 0, 1);

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code)
SELECT id, 'S', 'S', 'S' FROM attribute_definitions WHERE code = 'SIZE';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code)
SELECT id, 'M', 'M', 'M' FROM attribute_definitions WHERE code = 'SIZE';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code)
SELECT id, 'L', 'L', 'L' FROM attribute_definitions WHERE code = 'SIZE';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code)
SELECT id, 'Mint', 'MINT', 'MINT' FROM attribute_definitions WHERE code = 'COLOR';

INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code)
SELECT id, 'Noir', 'NOIR', 'NOIR' FROM attribute_definitions WHERE code = 'COLOR';

INSERT INTO product_families (article_family_id, code, label)
SELECT id, 'PULL_COL_ROND', 'Pull col rond' FROM article_families WHERE code = 'PULL';

INSERT INTO articles (family_id, code, label, article_type, default_unit)
SELECT id, 'AH25PLUMIERE', 'AH25PLUMIERE', 'FINISHED_GOOD', 'PIECE' FROM article_families WHERE code = 'PULL';

INSERT INTO articles (family_id, code, label, article_type, default_unit)
SELECT id, 'FIL-MINT', 'Fil couleur mint', 'COMPONENT', 'KG' FROM article_families WHERE code = 'FIL';

INSERT INTO articles (family_id, code, label, article_type, default_unit)
SELECT id, 'VCOMP-STD', 'Vignette composition standard', 'COMPONENT', 'PIECE' FROM article_families WHERE code = 'VIGNETTE_COMPOSITION';

INSERT INTO articles (family_id, code, label, article_type, default_unit)
SELECT id, 'SACHET-STD', 'Sachet standard', 'COMPONENT', 'PIECE' FROM article_families WHERE code = 'PACKAGING_SACHET';

INSERT INTO workcenters (code, label) VALUES
('WU-PMA25', 'Centre WU-PMA25'),
('WU-PO21', 'Centre WU-PO21'),
('WU-PMA33', 'Centre WU-PMA33'),
('WU-PMA16', 'Centre WU-PMA16'),
('WU-PMA01', 'Centre WU-PMA01'),
('WU-PMA38', 'Centre WU-PMA38'),
('WU-PMA41', 'Centre WU-PMA41');

INSERT INTO units_of_measure (code, label, unit_type, source_status) VALUES
('PIECE', 'Piece', 'COUNT', 'SIMULATED'),
('KG', 'Kilogramme', 'WEIGHT', 'SIMULATED'),
('MIN', 'Minute', 'TIME', 'HYPOTHESIS');

INSERT INTO unit_conversion_rules (from_unit_id, to_unit_id, factor, source_status, notes)
SELECT u1.id, u2.id, 1.0, 'SIMULATED', 'Conversion identite pour MVP local'
FROM units_of_measure u1
JOIN units_of_measure u2 ON u2.code = u1.code;
