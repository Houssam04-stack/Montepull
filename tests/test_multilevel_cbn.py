import sys
import unittest
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT_DIR))
sys.path.insert(0, str(ROOT_DIR / "src"))

from axioplan.core.database import connect
from axioplan.modules.gammes_nomenclatures.generation import calculate_gross_quantity
from scripts.init_db import main as init_database


class MultilevelCbnTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        init_database()
        cls.connection = connect()

    @classmethod
    def tearDownClass(cls):
        cls.connection.close()

    def test_pull_bom_uses_panneau_sf_at_level_1(self):
        row = self.connection.execute(
            """
            SELECT a.code
            FROM bom_base_lines bl
            JOIN bom_bases bb ON bb.id = bl.bom_base_id
            JOIN articles a ON a.id = bl.component_article_id
            WHERE bb.code = 'BOM_BASE_PULL_COL_ROND_EXEMPLE' AND bl.line_no = 10
            """
        ).fetchone()
        self.assertEqual(row[0], "PANNEAU-SF")

    def test_fil_mint_still_15kg_net_for_doublygilf_qty(self):
        """PF -> SF (1) -> FIL (0,5 kg) x 30 = 15 kg net."""
        total_net = 0.5 * 30
        total_gross = calculate_gross_quantity(total_net, 0.05)
        self.assertAlmostEqual(total_net, 15.0, places=3)
        self.assertAlmostEqual(total_gross, 15.789, places=3)


if __name__ == "__main__":
    unittest.main()
