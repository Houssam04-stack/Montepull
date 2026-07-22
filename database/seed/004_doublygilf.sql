-- Jeu de test DOUBLYGILF derive du fichier prod (PE260268 / AH25PLUMIERE M MINT, qte 30).
-- Fichiers source DOUBLYGILF non fournis dans le depot : A confirmer avec le responsable.

INSERT INTO sales_orders (code, customer_code, label, status, order_date)
VALUES ('DOUBLYGILF-OV-001', 'DOUBLYGILF', 'Commande test DOUBLYGILF — pull Plumiere', 'OPEN', '2026-07-06');

INSERT INTO sales_order_lines (sales_order_id, line_no, article_id, size_option_id, color_option_id, quantity, unit, product_family_id, external_ref)
SELECT so.id, 10, a.id, sz.id, col.id, 30, 'PIECE', pf.id, 'PE260268 / AH25PLUMIERE M MINT'
FROM sales_orders so
JOIN articles a ON a.code = 'AH25PLUMIERE'
JOIN attribute_options sz ON sz.attribute_id = (SELECT id FROM attribute_definitions WHERE code = 'SIZE') AND sz.technical_code = 'M'
JOIN attribute_options col ON col.attribute_id = (SELECT id FROM attribute_definitions WHERE code = 'COLOR') AND col.technical_code = 'MINT'
JOIN product_families pf ON pf.code = 'PULL_COL_ROND'
WHERE so.code = 'DOUBLYGILF-OV-001';
