-- Donnees pegging simulees : OA, OF, stock pour lier aux besoins CBN.
-- Regles d'allocation exactes : A confirmer avec le metier.

INSERT INTO purchase_orders (code, supplier_code, label, status, order_date)
VALUES ('OA-FIL-MINT-001', 'FOURN-FIL', 'Commande achat fil mint', 'OPEN', '2026-07-01');

INSERT INTO purchase_order_lines (purchase_order_id, line_no, article_id, quantity, unit, expected_date)
SELECT po.id, 10, a.id, 20.000, 'KG', '2026-07-15'
FROM purchase_orders po
JOIN articles a ON a.code = 'FIL-MINT'
WHERE po.code = 'OA-FIL-MINT-001';

INSERT INTO manufacturing_orders (code, article_id, quantity, unit, status, planned_start)
SELECT 'OF-PANNEAU-001', a.id, 25, 'PIECE', 'PLANNED', '2026-07-08'
FROM articles a WHERE a.code = 'PANNEAU-SF';

INSERT INTO manufacturing_orders (code, article_id, quantity, unit, status, sales_order_line_id, planned_start)
SELECT 'OF-PULL-DOUBLYGILF', a.id, 30, 'PIECE', 'PLANNED', sol.id, '2026-07-10'
FROM articles a
JOIN sales_order_lines sol ON sol.external_ref LIKE 'PE260268%'
JOIN sales_orders so ON so.id = sol.sales_order_id AND so.code = 'DOUBLYGILF-OV-001'
WHERE a.code = 'AH25PLUMIERE';

INSERT INTO stock_balances (article_id, quantity_available, unit)
SELECT a.id, 5.000, 'KG' FROM articles a WHERE a.code = 'FIL-MINT';

INSERT INTO stock_balances (article_id, quantity_available, unit)
SELECT a.id, 10.000, 'PIECE' FROM articles a WHERE a.code = 'PANNEAU-SF';
