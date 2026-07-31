"""
Recree la simulation TUNIMAPULF SIM + sync stocks vers sim_cbn_parameters.

Usage :
  python scripts/recreate_tunimapulf.py
"""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "src"))

from axioplan.core.database import connect  # noqa: E402


TUNIMA_NAME = "TUNIMAPULF SIM"
COMPONENTS = [
    ("AC240018", "Composant AC240018", 1.20, 1, 4),
    ("EL240112", "Composant EL240112", 0.05, 1, 2),
    ("EL240111", "Composant EL240111", 1.00, 0, 2),
    ("AV240014", "Composant AV240014", 1.00, 0, 3),
]


def ensure_simulation(cur) -> int:
    cur.execute("SELECT id FROM simulations WHERE name = ?", TUNIMA_NAME)
    row = cur.fetchone()
    if row:
        return int(row[0])
    cur.execute(
        """
        INSERT INTO simulations (name, description, created_by, status, is_active)
        OUTPUT INSERTED.id
        VALUES (?, ?, 'script', 'DRAFT', 1)
        """,
        TUNIMA_NAME,
        "Simulation Montepull — duplication taille/couleur + CBN",
    )
    return int(cur.fetchone()[0])


def seed_if_needed(cur, sim_id: int) -> None:
    cur.execute(
        "SELECT COUNT(*) FROM sim_articles WHERE simulation_id = ? AND code = 'TUNIMAPULF_BASE'",
        sim_id,
    )
    if int(cur.fetchone()[0]) > 0:
        print(f"TUNIMAPULF_BASE deja present (simulation {sim_id})")
        return

    def insert_article(code, designation, article_type, procurement, lead, is_template=0):
        cur.execute(
            """
            INSERT INTO sim_articles
              (simulation_id, code, designation, article_type, is_template, is_generated,
               procurement_type, lead_time_days, unit, is_simulated)
            OUTPUT INSERTED.id
            VALUES (?, ?, ?, ?, ?, 0, ?, ?, 'UN', 1)
            """,
            sim_id,
            code,
            designation,
            article_type,
            is_template,
            procurement,
            lead,
        )
        return int(cur.fetchone()[0])

    base_id = insert_article(
        "TUNIMAPULF_BASE", "TUNIMAPULF (template)", "FinishedGood", "Manufactured", 5, 1
    )
    component_ids = {}
    for code, label, _qty, _size, lead in COMPONENTS:
        cur.execute("SELECT label FROM articles WHERE code = ?", code)
        row = cur.fetchone()
        designation = row[0] if row and row[0] else label
        component_ids[code] = insert_article(code, designation, "Purchased", "Purchased", lead)

    for code, _label, qty, size_coef, _lead in COMPONENTS:
        cur.execute(
            """
            INSERT INTO sim_bom_lines
              (simulation_id, parent_article_id, component_article_id, quantity_per, scrap_rate,
               offset_days, apply_size_coefficient, apply_color_substitution, is_generated,
               is_simulated, nomenclature_type, alternative)
            VALUES (?, ?, ?, ?, 0, 0, ?, 0, 0, 1, 'BASE', 0)
            """,
            sim_id,
            base_id,
            component_ids[code],
            qty,
            size_coef,
        )

    for no, name, run, size_coef in [
        (10, "Preparation", 8, 1),
        (20, "Assemblage", 20, 1),
        (30, "Controle", 3, 0),
        (40, "Emballage", 2, 0),
    ]:
        cur.execute(
            """
            INSERT INTO sim_routing_operations
              (simulation_id, article_id, operation_number, operation_name, work_center,
               setup_time_minutes, run_time_minutes, queue_time_minutes, move_time_minutes,
               apply_size_coefficient, apply_color_coefficient, is_generated, is_simulated)
            VALUES (?, ?, ?, ?, 'ATELIER', 0, ?, 0, 0, ?, 0, 0, 1)
            """,
            sim_id,
            base_id,
            no,
            name,
            run,
            size_coef,
        )

    def insert_param(article_id, lead):
        cur.execute(
            """
            INSERT INTO sim_cbn_parameters
              (simulation_id, article_id, on_hand_stock, safety_stock, reserved_quantity,
               scheduled_receipt_production, scheduled_receipt_purchase, lead_time_days,
               lot_rule, min_lot, multiple_lot, is_simulated)
            VALUES (?, ?, 0, 0, 0, 0, 0, ?, 'LotForLot', 0, 1, 1)
            """,
            sim_id,
            article_id,
            lead,
        )

    insert_param(base_id, 5)
    for code, _l, _q, _s, lead in COMPONENTS:
        insert_param(component_ids[code], lead)

    cur.execute("SELECT COUNT(*) FROM sim_duplication_options WHERE simulation_id = ?", sim_id)
    if int(cur.fetchone()[0]) == 0:
        sizes = [("S", 0.90), ("M", 1.00), ("L", 1.15), ("XL", 1.30)]
        colors = [("Noir", 1.0), ("Bleu", 1.0)]
        for i, (value, coef) in enumerate(sizes):
            cur.execute(
                """
                INSERT INTO sim_duplication_options
                  (simulation_id, option_type, value, coefficient, is_selected, sort_order)
                VALUES (?, 'SIZE', ?, ?, 1, ?)
                """,
                sim_id,
                value,
                coef,
                i,
            )
        for i, (value, coef) in enumerate(colors):
            cur.execute(
                """
                INSERT INTO sim_duplication_options
                  (simulation_id, option_type, value, coefficient, is_selected, sort_order)
                VALUES (?, 'COLOR', ?, ?, 1, ?)
                """,
                sim_id,
                value,
                coef,
                i,
            )

    print(f"Seed TUNIMAPULF_BASE OK (simulation {sim_id})")


def sync_stocks(cur, sim_id: int) -> int:
    cur.execute(
        """
        UPDATE p
        SET on_hand_stock = sb.quantity_available
        FROM sim_cbn_parameters p
        INNER JOIN sim_articles sa ON sa.id = p.article_id AND sa.simulation_id = p.simulation_id
        INNER JOIN articles a ON a.code = sa.code
        INNER JOIN stock_balances sb ON sb.article_id = a.id
        WHERE p.simulation_id = ?
        """,
        sim_id,
    )
    return cur.rowcount


def main() -> None:
    cn = connect()
    cn.autocommit = False
    cur = cn.cursor()
    try:
        sim_id = ensure_simulation(cur)
        seed_if_needed(cur, sim_id)
        updated = sync_stocks(cur, sim_id)
        cn.commit()
        print(f"Simulation id={sim_id} name={TUNIMA_NAME}")
        print(f"Parametres CBN synchronises depuis stock_balances : {updated}")
        print("Ouvrir : http://localhost:5280/simulation-mrp?ensure=tunimapulf&tab=duplication")
    except Exception:
        cn.rollback()
        raise
    finally:
        cur.close()
        cn.close()


if __name__ == "__main__":
    main()
