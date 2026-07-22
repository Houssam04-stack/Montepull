import sys
import unittest
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT_DIR))
sys.path.insert(0, str(ROOT_DIR / "src"))

from axioplan.core.database import connect
from scripts.init_db import main as init_database


class ArticleConfiguratorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        init_database()
        cls.connection = connect()

    @classmethod
    def tearDownClass(cls):
        cls.connection.close()

    def test_article_configurator_tables_exist(self):
        tables = [
            "customers",
            "article_family_attributes",
            "attribute_formatting_rules",
            "customer_article_family_configurations",
            "customer_article_family_attributes",
            "configuration_sessions",
            "configuration_inputs",
            "article_duplication_sessions",
            "article_duplication_changes",
        ]
        for table in tables:
            row = self.connection.execute(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = ?",
                (table,),
            ).fetchone()
            self.assertEqual(row[0], 1, f"Table manquante : {table}")

    def test_customers_seeded(self):
        count = self.connection.execute("SELECT COUNT(*) FROM customers").fetchone()[0]
        self.assertGreaterEqual(count, 2)

    def test_vignette_family_has_matrix(self):
        count = self.connection.execute(
            """
            SELECT COUNT(*)
            FROM customer_article_family_attributes ca
            JOIN customer_article_family_configurations cfg ON cfg.id = ca.configuration_id
            JOIN customers c ON c.id = cfg.customer_id
            JOIN article_families af ON af.id = cfg.family_id
            WHERE c.code = 'CLIENT_A' AND af.code = 'VIGNETTE_COMPOSITION'
            """
        ).fetchone()[0]
        self.assertGreaterEqual(count, 5)

    def test_formatting_rules_exist(self):
        count = self.connection.execute("SELECT COUNT(*) FROM attribute_formatting_rules").fetchone()[0]
        self.assertGreaterEqual(count, 4)

    def test_articles_have_creation_mode_column(self):
        row = self.connection.execute(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'articles' AND COLUMN_NAME = 'creation_mode'
            """
        ).fetchone()
        self.assertEqual(row[0], 1)


class AttributeValueFormatterTests(unittest.TestCase):
    def test_formatter_import_from_domain_via_python_mirror(self):
        """Vérifie la logique de formatage documentée (miroir Python minimal)."""
        from axioplan.modules.articles.formatting import format_attribute_value

        result = format_attribute_value(
            " bleu marine ",
            case_rule="TITLE",
            strip_accents_for_code=True,
        )
        self.assertEqual(result["display_value"], "Bleu Marine")
        self.assertEqual(result["technical_code"], "BLEU-MARINE")

        result_size = format_attribute_value(" xxl ", remove_internal_spaces=True, case_rule="UPPER")
        self.assertEqual(result_size["normalized_value"], "XXL")
        self.assertEqual(result_size["technical_code"], "XXL")


if __name__ == "__main__":
    unittest.main()
