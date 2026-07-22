import sys
import unittest
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT_DIR))
sys.path.insert(0, str(ROOT_DIR / "src"))

from axioplan.core.database import connect
from scripts.init_db import main as init_database


class ParameterTablesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        init_database()
        cls.connection = connect()

    @classmethod
    def tearDownClass(cls):
        cls.connection.close()

    def test_mvp_parameter_tables_exist(self):
        for table in ("mvp_parameter_groups", "mvp_parameters", "mvp_parameter_values"):
            row = self.connection.execute(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = ?",
                (table,),
            ).fetchone()
            self.assertEqual(row[0], 1, f"Table manquante : {table}")

    def test_cbn_parameters_seeded(self):
        count = self.connection.execute("SELECT COUNT(*) FROM mvp_parameters").fetchone()[0]
        self.assertGreaterEqual(count, 3)

    def test_consumption_coefficients_updatable(self):
        cursor = self.connection.cursor()
        row = cursor.execute(
            """
            SELECT TOP 1 cc.id, cc.coefficient
            FROM consumption_coefficients cc
            JOIN product_families pf ON pf.id = cc.product_family_id
            WHERE pf.code = 'PULL_COL_ROND' AND cc.status = 'VALIDATED'
            """
        ).fetchone()
        self.assertIsNotNone(row)
        coeff_id, original = int(row[0]), float(row[1])
        new_value = original + 0.01 if original < 2 else original - 0.01

        cursor.execute(
            "UPDATE consumption_coefficients SET coefficient = ? WHERE id = ?",
            (new_value, coeff_id),
        )
        self.connection.commit()

        updated = cursor.execute(
            "SELECT coefficient FROM consumption_coefficients WHERE id = ?",
            (coeff_id,),
        ).fetchone()[0]
        self.assertAlmostEqual(float(updated), new_value, places=5)

        cursor.execute(
            "UPDATE consumption_coefficients SET coefficient = ? WHERE id = ?",
            (original, coeff_id),
        )
        self.connection.commit()

    def test_bom_loss_rate_updatable(self):
        cursor = self.connection.cursor()
        row = cursor.execute(
            """
            SELECT TOP 1 bl.id, bl.loss_rate
            FROM bom_base_lines bl
            JOIN bom_bases bb ON bb.id = bl.bom_base_id
            JOIN product_families pf ON pf.id = bb.product_family_id
            WHERE pf.code = 'PULL_COL_ROND'
            ORDER BY bl.line_no
            """
        ).fetchone()
        self.assertIsNotNone(row)
        line_id, original = int(row[0]), float(row[1])

        cursor.execute(
            "UPDATE bom_base_lines SET loss_rate = ? WHERE id = ?",
            (0.08, line_id),
        )
        self.connection.commit()

        updated = cursor.execute(
            "SELECT loss_rate FROM bom_base_lines WHERE id = ?",
            (line_id,),
        ).fetchone()[0]
        self.assertAlmostEqual(float(updated), 0.08, places=5)

        cursor.execute(
            "UPDATE bom_base_lines SET loss_rate = ? WHERE id = ?",
            (original, line_id),
        )
        self.connection.commit()


if __name__ == "__main__":
    unittest.main()
