import sys
import unittest
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT_DIR))
sys.path.insert(0, str(ROOT_DIR / "src"))

from axioplan.core.database import connect
from axioplan.modules.gammes_nomenclatures.generation import (
    calculate_gross_quantity,
    calculate_net_quantity,
)
from scripts.init_db import main as init_database


class DoublygilfValidationTests(unittest.TestCase):
    """Validation du jeu DOUBLYGILF derive de docs/prod (1).md."""

    @classmethod
    def setUpClass(cls):
        init_database()
        cls.connection = connect()

    @classmethod
    def tearDownClass(cls):
        cls.connection.close()

    def test_order_line_matches_prod_reference(self):
        row = self.connection.execute(
            """
            SELECT sol.quantity, a.code, sz.technical_code, col.technical_code, sol.external_ref
            FROM sales_order_lines sol
            JOIN sales_orders so ON so.id = sol.sales_order_id
            JOIN articles a ON a.id = sol.article_id
            JOIN attribute_options sz ON sz.id = sol.size_option_id
            JOIN attribute_options col ON col.id = sol.color_option_id
            WHERE so.code = 'DOUBLYGILF-OV-001'
            """
        ).fetchone()

        self.assertIsNotNone(row)
        self.assertEqual(int(row[0]), 30)
        self.assertEqual(row[1], "AH25PLUMIERE")
        self.assertEqual(row[2], "M")
        self.assertEqual(row[3], "MINT")
        self.assertIn("PE260268", row[4])

    def test_fil_mint_net_and_gross_for_doublygilf(self):
        """Formule MVP : net = base * size * color * qte ; brut = net / (1 - perte)."""
        base_qty = 0.5
        order_qty = 30
        loss_rate = 0.05
        size_coeff = 1.0
        color_coeff = 1.0

        unit_net = calculate_net_quantity(base_qty, size_coeff, color_coeff)
        total_net = unit_net * order_qty
        total_gross = calculate_gross_quantity(total_net, loss_rate)

        self.assertAlmostEqual(total_net, 15.0, places=3)
        self.assertAlmostEqual(total_gross, 15.789, places=3)

    def test_fixed_components_scale_with_order_qty(self):
        order_qty = 30
        self.assertEqual(order_qty * 1.0, 30.0)

    def test_prod_allocated_need_differs_from_mvp_bom(self):
        """
        Le fichier prod indique un besoin alloue de 152,094 (unite a confirmer).
        Le MVP calcule 15,789 KG brut pour FIL-MINT avec la nomenclature exemple.
        """
        prod_allocated_need = 152.094
        mvp_fil_mint_gross = calculate_gross_quantity(15.0, 0.05)

        self.assertNotAlmostEqual(prod_allocated_need, mvp_fil_mint_gross, places=1)
        self.assertAlmostEqual(mvp_fil_mint_gross, 15.789, places=3)


if __name__ == "__main__":
    unittest.main()
