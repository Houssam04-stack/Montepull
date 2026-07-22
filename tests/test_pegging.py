import sys
import unittest
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT_DIR))
sys.path.insert(0, str(ROOT_DIR / "src"))

from axioplan.core.database import connect
from scripts.init_db import main as init_database


class PeggingTablesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        init_database()
        cls.connection = connect()

    @classmethod
    def tearDownClass(cls):
        cls.connection.close()

    def test_pegging_tables_exist(self):
        for table in (
            "purchase_orders",
            "manufacturing_orders",
            "stock_balances",
            "pegging_runs",
            "pegging_links",
            "pegging_link_versions",
            "article_bom_assignments",
            "application_logs",
        ):
            row = self.connection.execute(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = ?",
                (table,),
            ).fetchone()
            self.assertEqual(row[0], 1, table)

    def test_multilevel_bom_seeded(self):
        row = self.connection.execute(
            "SELECT COUNT(*) FROM article_bom_assignments aba JOIN articles a ON a.id = aba.article_id WHERE a.code = 'PANNEAU-SF'"
        ).fetchone()
        self.assertEqual(row[0], 1)

    def test_pegging_seed_data_loaded(self):
        oa = self.connection.execute("SELECT COUNT(*) FROM purchase_orders").fetchone()[0]
        of_count = self.connection.execute("SELECT COUNT(*) FROM manufacturing_orders").fetchone()[0]
        stock = self.connection.execute("SELECT COUNT(*) FROM stock_balances").fetchone()[0]
        self.assertGreaterEqual(oa, 1)
        self.assertGreaterEqual(of_count, 2)
        self.assertGreaterEqual(stock, 2)


if __name__ == "__main__":
    unittest.main()
