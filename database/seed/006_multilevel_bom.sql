-- Nomenclature multi-niveaux MVP : PF -> SF (panneau) -> composant (fil).
-- Conforme au flux documente : cascade PF -> SF -> composants.
-- Les quantites fil restent equivalentes au BOM mono-niveau (0,5 kg fil / panneau x 1 panneau / pull).

INSERT INTO article_families (category_id, code, label)
SELECT id, 'PANNEAU', 'Panneau tricote' FROM article_categories WHERE code = 'SEMI_FINISHED';

INSERT INTO articles (family_id, code, label, article_type, default_unit)
SELECT id, 'PANNEAU-SF', 'Panneau semi-fini standard', 'SEMI_FINISHED', 'PIECE'
FROM article_families WHERE code = 'PANNEAU';

INSERT INTO bom_bases (product_family_id, code, version, status)
SELECT pf.id, 'BOM_PANNEAU_SF', 1, 'DRAFT'
FROM product_families pf
WHERE pf.code = 'PULL_COL_ROND';

INSERT INTO bom_base_lines (bom_base_id, line_no, component_article_id, quantity_base, unit, loss_rate, behavior)
SELECT b.id, 10, a.id, 0.500, 'KG', 0.05, 'CALCULATED'
FROM bom_bases b
JOIN articles a ON a.code = 'FIL-MINT'
WHERE b.code = 'BOM_PANNEAU_SF';

INSERT INTO article_bom_assignments (article_id, bom_base_id)
SELECT a.id, b.id
FROM articles a
JOIN bom_bases b ON b.code = 'BOM_PANNEAU_SF'
WHERE a.code = 'PANNEAU-SF';

-- Remplacer FIL-MINT direct par PANNEAU-SF au niveau pull (niveau 1).
UPDATE bl
SET component_article_id = a_sf.id, quantity_base = 1.000, unit = 'PIECE', loss_rate = 0.00, behavior = 'FIXED'
FROM bom_base_lines bl
JOIN bom_bases bb ON bb.id = bl.bom_base_id
JOIN articles a_fil ON a_fil.id = bl.component_article_id AND a_fil.code = 'FIL-MINT'
JOIN articles a_sf ON a_sf.code = 'PANNEAU-SF'
WHERE bb.code = 'BOM_BASE_PULL_COL_ROND_EXEMPLE' AND bl.line_no = 10;
