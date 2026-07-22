import sys
import unittest
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT_DIR))
sys.path.insert(0, str(ROOT_DIR / "src"))

from axioplan.core.database import connect
from scripts.init_db import main as init_database


class CbnTablesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        init_database()
        cls.connection = connect()

    @classmethod
    def tearDownClass(cls):
        cls.connection.close()

    def test_cbn_tables_exist(self):
        for table in (
            "sales_orders",
            "sales_order_lines",
            "cbn_runs",
            "cbn_flattened_bom_lines",
            "cbn_material_requirements",
            "cbn_calculation_traces",
        ):
            row = self.connection.execute(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = ?",
                (table,),
            ).fetchone()
            self.assertEqual(row[0], 1, f"Table manquante : {table}")

    def test_doublygilf_order_seeded(self):
        row = self.connection.execute(
            "SELECT code FROM sales_orders WHERE code = 'DOUBLYGILF-OV-001'"
        ).fetchone()
        self.assertIsNotNone(row)

    def test_cbn_run_persists_results(self):
        cursor = self.connection.cursor()
        order_id = cursor.execute(
            "SELECT id FROM sales_orders WHERE code = 'DOUBLYGILF-OV-001'"
        ).fetchone()[0]
        family_id = cursor.execute(
            "SELECT id FROM product_families WHERE code = 'PULL_COL_ROND'"
        ).fetchone()[0]
        parent_id = cursor.execute(
            """
            SELECT TOP 1 a.id
            FROM articles a
            JOIN article_families af ON af.id = a.family_id
            JOIN product_families pf ON pf.article_family_id = af.id
            WHERE pf.code = 'PULL_COL_ROND' AND a.article_type = 'FINISHED_GOOD'
            """
        ).fetchone()[0]
        component_id = cursor.execute(
            "SELECT id FROM articles WHERE code = 'FIL-MINT'"
        ).fetchone()[0]

        cursor.execute(
            "INSERT INTO cbn_runs (sales_order_id, product_family_id, status) OUTPUT INSERTED.id VALUES (?, ?, 'COMPLETED')",
            (order_id, family_id),
        )
        run_id = int(cursor.fetchone()[0])
        cursor.execute(
            """
            INSERT INTO cbn_flattened_bom_lines (
                cbn_run_id, parent_article_id, component_article_id, bom_line_no,
                quantity_per_unit, unit, loss_rate, behavior, source_path
            ) VALUES (?, ?, ?, 10, 0.5, 'KG', 0.05, 'CALCULATED', 'BOM:10')
            """,
            (run_id, parent_id, component_id),
        )
        cursor.execute(
            """
            INSERT INTO cbn_material_requirements (
                cbn_run_id, component_article_id, variant_label,
                quantity_net, quantity_gross, unit, source_path
            ) VALUES (?, ?, 'AH25PLUMIERE-M-MINT', 15.0, 15.789, 'KG', 'BOM:10')
            """,
            (run_id, component_id),
        )
        self.connection.commit()

        count = self.connection.execute(
            "SELECT COUNT(*) FROM cbn_material_requirements WHERE cbn_run_id = ?",
            (run_id,),
        ).fetchone()[0]
        self.assertEqual(count, 1)


if __name__ == "__main__":
    unittest.main()
