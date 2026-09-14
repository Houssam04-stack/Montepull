import sys
import unittest
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT_DIR))
sys.path.insert(0, str(ROOT_DIR / "src"))

from axioplan.core.database import connect
from scripts.init_db import DEFAULT_DATABASE, main as init_database


class DatabaseMvpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        init_database()
        cls.connection = connect()

    @classmethod
    def tearDownClass(cls):
        cls.connection.close()

    def test_init_db_creates_database(self):
        row = self.connection.execute(
            "SELECT DB_ID(?)",
            (DEFAULT_DATABASE,),
        ).fetchone()
        self.assertIsNotNone(row[0])

    def test_seed_data_is_loaded(self):
        article_count = self.connection.execute("SELECT COUNT(*) FROM articles").fetchone()[0]
        unit_count = self.connection.execute("SELECT COUNT(*) FROM units_of_measure").fetchone()[0]
        profile_count = self.connection.execute("SELECT COUNT(*) FROM generation_profiles").fetchone()[0]

        self.assertGreaterEqual(article_count, 4)
        self.assertGreaterEqual(unit_count, 3)
        self.assertEqual(profile_count, 1)

    def test_prod_operations_are_loaded(self):
        operation_count = self.connection.execute("SELECT COUNT(*) FROM routing_base_operations ops "
            "JOIN routing_bases rb ON rb.id = ops.routing_base_id "
            "WHERE rb.code = 'GAM_BASE_PULL_COL_ROND_PROD_EXEMPLE'").fetchone()[0]

        self.assertEqual(operation_count, 30)

    def test_only_validated_profiles_are_sql_usable(self):
        cursor = self.connection.cursor()
        cursor.execute("DELETE FROM generation_profiles WHERE code = 'PROFILE_BLOCKED_TEST'")
        cursor.execute(
            "INSERT INTO generation_profiles (product_family_id, code, label, status) "
            "SELECT id, 'PROFILE_BLOCKED_TEST', 'Profil bloque test', 'BLOCKED' "
            "FROM product_families WHERE code = 'PULL_COL_ROND'"
        )
        self.connection.commit()

        usable_statuses = [
            row[0]
            for row in self.connection.execute("SELECT status FROM usable_generation_profiles").fetchall()
        ]

        self.assertEqual(set(usable_statuses), {"VALIDATED"})

        cursor.execute("DELETE FROM generation_profiles WHERE code = 'PROFILE_BLOCKED_TEST'")
        self.connection.commit()

    def test_create_calculation_trace(self):
        cursor = self.connection.cursor()
        cursor.execute(
            "INSERT INTO generation_sessions (profile_id, mode, status) "
            "OUTPUT INSERTED.id "
            "SELECT id, 'GENERATION', 'GENERATED' FROM generation_profiles WHERE code = 'PROFILE_SIZE_COLOR'"
        )
        session_id = int(cursor.fetchone()[0])
        cursor.execute(
            """
            INSERT INTO calculation_traces (
                session_id,
                object_type,
                object_id,
                rule_code,
                source_table,
                source_id,
                source_value,
                source_unit,
                size_coefficient,
                color_coefficient,
                loss_rate,
                result_value,
                result_unit,
                formula,
                details
            )
            OUTPUT INSERTED.id
            VALUES (?, 'BOM_LINE', 1, 'TEST_TRACE', 'bom_base_lines', 1, 0.5, 'KG', 1.1, 1.02, 0.05, 0.591, 'KG',
                    'base * size * color / (1 - loss)', 'Trace test MVP')
            """,
            (session_id,),
        )
        trace_id = int(cursor.fetchone()[0])
        self.connection.commit()

        trace = self.connection.execute(
            "SELECT rule_code, result_value, result_unit FROM calculation_traces WHERE id = ?",
            (trace_id,),
        ).fetchone()

        self.assertEqual(trace[0], "TEST_TRACE")
        self.assertEqual(round(trace[1], 3), 0.591)
        self.assertEqual(trace[2], "KG")

    def test_generate_flattened_views(self):
        cursor = self.connection.cursor()
        cursor.execute(
            "INSERT INTO generation_sessions (profile_id, mode, status) "
            "OUTPUT INSERTED.id "
            "SELECT id, 'GENERATION', 'GENERATED' FROM generation_profiles WHERE code = 'PROFILE_SIZE_COLOR'"
        )
        session_id = int(cursor.fetchone()[0])
        cursor.execute(
            """
            INSERT INTO generated_variants (session_id, article_code, size_code, color_code)
            OUTPUT INSERTED.id
            VALUES (?, 'AH25PLUMIERE-L-NOIR', 'L', 'NOIR')
            """,
            (session_id,),
        )
        variant_id = int(cursor.fetchone()[0])
        cursor.execute(
            "INSERT INTO bom_generated_versions (variant_id, source_bom_base_id, status) "
            "OUTPUT INSERTED.id "
            "SELECT ?, id, 'GENERATED' FROM bom_bases WHERE code = 'BOM_BASE_PULL_COL_ROND_EXEMPLE'",
            (variant_id,),
        )
        bom_generated_version_id = int(cursor.fetchone()[0])
        cursor.execute(
            "INSERT INTO routing_generated_versions (variant_id, source_routing_base_id, status) "
            "OUTPUT INSERTED.id "
            "SELECT ?, id, 'GENERATED' FROM routing_bases WHERE code = 'GAM_BASE_PULL_COL_ROND_PROD_EXEMPLE'",
            (variant_id,),
        )
        routing_generated_version_id = int(cursor.fetchone()[0])
        cursor.execute(
            """
            INSERT INTO flattened_bom_lines (
                bom_generated_version_id,
                generation_session_id,
                component_article_id,
                total_quantity_net,
                total_quantity_gross,
                unit,
                status,
                source_version,
                recalculation_reason,
                source_path
            )
            SELECT ?, ?, id, 0.561, 0.591, 'KG', 'GENERATED', 'BOM_BASE_PULL_COL_ROND_EXEMPLE_V1',
                   'TEST_MVP', 'BOM:10'
            FROM articles
            WHERE code = 'FIL-MINT'
            """,
            (bom_generated_version_id, session_id),
        )
        cursor.execute(
            """
            INSERT INTO flattened_routing_lines (
                routing_generated_version_id,
                generation_session_id,
                workcenter_id,
                total_time,
                time_unit,
                status,
                source_version,
                recalculation_reason,
                source_path
            )
            SELECT ?, ?, id, 735.0, 'TO_CONFIRM', 'GENERATED', 'GAM_BASE_PULL_COL_ROND_PROD_EXEMPLE_V1',
                   'TEST_MVP', 'ROUTING:WU-PMA01'
            FROM workcenters
            WHERE code = 'WU-PMA01'
            """,
            (routing_generated_version_id, session_id),
        )
        self.connection.commit()

        flattened_bom_count = self.connection.execute(
            "SELECT COUNT(*) FROM flattened_bom_lines WHERE generation_session_id = ?",
            (session_id,),
        ).fetchone()[0]
        flattened_routing_count = self.connection.execute(
            "SELECT COUNT(*) FROM flattened_routing_lines WHERE generation_session_id = ?",
            (session_id,),
        ).fetchone()[0]

        self.assertEqual(flattened_bom_count, 1)
        self.assertEqual(flattened_routing_count, 1)


if __name__ == "__main__":
    unittest.main()
