"""
Importe liste_stocks.xlsx dans AxioplanMvp :
  - cree/maj les articles manquants (famille IMPORTED_COMPONENT)
  - importe chaque ligne Excel (lot / emplacement) dans stock_lot_lines
  - agregge par code article dans stock_balances (pegging / CBN legacy)
  - synchronise sim_cbn_parameters (stock + reserve) pour les simulations existantes
"""
from __future__ import annotations

import argparse
import os
import sys
from collections import defaultdict
from datetime import date, datetime
from pathlib import Path

import openpyxl
import pyodbc

ROOT_DIR = Path(__file__).resolve().parents[1]
DEFAULT_SERVER = r"localhost\SQLEXPRESS"
DEFAULT_DATABASE = "AxioplanMvp"
DEFAULT_CONNECTION_STRING = (
    "Driver={ODBC Driver 18 for SQL Server};"
    f"Server={DEFAULT_SERVER};Database={DEFAULT_DATABASE};"
    "Trusted_Connection=yes;TrustServerCertificate=yes;"
)

COLUMN_ALIASES = {
    "Désignation": ["Désignation", "Designation", "D\xe9signation"],
    "Quantité en stock": ["Quantité en stock", "Quantite en stock"],
    "Quantité allouée": ["Quantité allouée", "Quantite allouee", "Quantité allouee"],
    "Quantité en cours": ["Quantité en cours", "Quantite en cours"],
    "Écart disponible": ["Écart disponible", "Ecart disponible", "\xc9cart disponible"],
    "Unité": ["Unité", "Unite", "Unit\xe9"],
    "Emplacement": ["Emplacement"],
    "Entrepôt": ["Entrepôt", "Entrepot"],
    "Lot": ["Lot"],
    "Lot Fournisseur": ["Lot Fournisseur", "Lot fournisseur"],
    "Statut": ["Statut"],
    "Type Emplacement": ["Type Emplacement", "Type emplacement"],
    "Date de réception": ["Date de réception", "Date de reception"],
    "Dernière réception": ["Dernière réception", "Derniere reception", "Derni\u00e8re r\u00e9ception"],
    "Dernière sortie": ["Dernière sortie", "Derniere sortie"],
}


def get_connection_string() -> str:
    return os.environ.get("AXIOPLAN_CONNECTION_STRING", DEFAULT_CONNECTION_STRING)


def normalize_unit(raw: str | None) -> str:
    unit = (raw or "").strip().upper()
    if not unit:
        return "UN"
    if unit in ("UN", "U", "PCS", "PC", "PIECE", "PIÈCE"):
        return "UN"
    return unit


def normalize_text(value) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def parse_excel_date(value) -> date | None:
    if value is None:
        return None
    if isinstance(value, datetime):
        parsed = value.date()
    elif isinstance(value, date):
        parsed = value
    else:
        return None
    if parsed.year < 1900:
        return None
    return parsed


def resolve_columns(headers: list[str]) -> dict[str, int | None]:
    idx = {h: i for i, h in enumerate(headers)}

    def col(name: str, optional: bool = False) -> int | None:
        for alias in COLUMN_ALIASES.get(name, [name]):
            if alias in idx:
                return idx[alias]
        if optional:
            return None
        key = name.split()[0][:5].lower()
        for header, column_index in idx.items():
            if key in header.lower().replace("é", "e").replace("è", "e"):
                return column_index
        if optional:
            return None
        raise KeyError(f"Colonne introuvable: {name}. Headers={headers}")

    return {
        "article": col("Article"),
        "designation": col("Désignation"),
        "stock": col("Quantité en stock"),
        "unit": col("Unité"),
        "allocated": col("Quantité allouée", optional=True),
        "in_progress": col("Quantité en cours", optional=True),
        "available": col("Écart disponible", optional=True),
        "location": col("Emplacement", optional=True),
        "warehouse": col("Entrepôt", optional=True),
        "lot": col("Lot", optional=True),
        "supplier_lot": col("Lot Fournisseur", optional=True),
        "status": col("Statut", optional=True),
        "location_type": col("Type Emplacement", optional=True),
        "received_date": col("Date de réception", optional=True),
        "last_receipt_date": col("Dernière réception", optional=True),
        "last_issue_date": col("Dernière sortie", optional=True),
    }


def load_stock_lines(xlsx_path: Path) -> list[dict]:
    wb = openpyxl.load_workbook(xlsx_path, data_only=True)
    if "Stocks" not in wb.sheetnames:
        raise RuntimeError(f"Feuille 'Stocks' introuvable dans {xlsx_path}")
    ws = wb["Stocks"]
    headers = [str(c.value).strip() if c.value is not None else "" for c in next(ws.iter_rows(min_row=1, max_row=1))]
    columns = resolve_columns(headers)

    lines: list[dict] = []
    for row in ws.iter_rows(min_row=2, values_only=True):
        raw_code = row[columns["article"]]
        if raw_code is None or str(raw_code).strip() == "":
            continue

        code = str(raw_code).strip().upper()
        label = str(row[columns["designation"]] or code).strip()
        qty_stock = float(row[columns["stock"]] or 0)
        qty_alloc = float(row[columns["allocated"]] or 0) if columns["allocated"] is not None else 0.0
        qty_progress = float(row[columns["in_progress"]] or 0) if columns["in_progress"] is not None else 0.0
        if columns["available"] is not None and row[columns["available"]] is not None and str(row[columns["available"]]).strip() != "":
            qty_available = float(row[columns["available"]])
        else:
            qty_available = qty_stock - qty_alloc
        unit = normalize_unit(str(row[columns["unit"]]) if row[columns["unit"]] is not None else None)

        lines.append(
            {
                "code": code,
                "label": label,
                "location_code": normalize_text(row[columns["location"]]) if columns["location"] is not None else None,
                "warehouse": normalize_text(row[columns["warehouse"]]) if columns["warehouse"] is not None else None,
                "lot_code": normalize_text(row[columns["lot"]]) if columns["lot"] is not None else None,
                "supplier_lot": normalize_text(row[columns["supplier_lot"]]) if columns["supplier_lot"] is not None else None,
                "quantity_allocated": qty_alloc,
                "quantity_in_progress": qty_progress,
                "quantity_in_stock": qty_stock,
                "quantity_available": qty_available,
                "status": normalize_text(row[columns["status"]]) if columns["status"] is not None else None,
                "location_type": normalize_text(row[columns["location_type"]]) if columns["location_type"] is not None else None,
                "unit": unit,
                "received_date": parse_excel_date(row[columns["received_date"]]) if columns["received_date"] is not None else None,
                "last_receipt_date": parse_excel_date(row[columns["last_receipt_date"]]) if columns["last_receipt_date"] is not None else None,
                "last_issue_date": parse_excel_date(row[columns["last_issue_date"]]) if columns["last_issue_date"] is not None else None,
            }
        )

    return lines


def aggregate_stock_lines(lines: list[dict]) -> dict[str, dict]:
    agg: dict[str, dict] = defaultdict(
        lambda: {"label": "", "qty_stock": 0.0, "qty_alloc": 0.0, "qty_available": 0.0, "unit": "UN", "rows": 0}
    )
    for line in lines:
        code = line["code"]
        entry = agg[code]
        entry["qty_stock"] += float(line["quantity_in_stock"])
        entry["qty_alloc"] += float(line["quantity_allocated"])
        entry["qty_available"] += float(line["quantity_available"])
        entry["unit"] = line["unit"] or entry["unit"]
        label = line["label"]
        if label and (not entry["label"] or len(label) > len(entry["label"])):
            entry["label"] = label
        entry["rows"] += 1
    return dict(agg)


def load_aggregated_stocks(xlsx_path: Path) -> dict[str, dict]:
    return aggregate_stock_lines(load_stock_lines(xlsx_path))


def ensure_imported_family(cur: pyodbc.Cursor) -> int:
    cur.execute("SELECT id FROM article_families WHERE code = ?", "IMPORTED_COMPONENT")
    row = cur.fetchone()
    if row:
        return int(row[0])

    cur.execute("SELECT id FROM article_categories WHERE code = ?", "MAIN_MATERIAL")
    cat = cur.fetchone()
    if not cat:
        raise RuntimeError("Categorie MAIN_MATERIAL introuvable.")
    cur.execute(
        """
        INSERT INTO article_families (category_id, code, label)
        OUTPUT INSERTED.id
        VALUES (?, 'IMPORTED_COMPONENT', 'Composant importe')
        """,
        int(cat[0]),
    )
    return int(cur.fetchone()[0])


def ensure_stock_lot_lines_table(cur: pyodbc.Cursor) -> None:
    cur.execute(
        """
        IF OBJECT_ID(N'dbo.stock_lot_lines', N'U') IS NULL
        BEGIN
            CREATE TABLE stock_lot_lines (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                article_id INT NOT NULL,
                location_code NVARCHAR(50) NULL,
                warehouse NVARCHAR(50) NULL,
                lot_code NVARCHAR(80) NULL,
                supplier_lot NVARCHAR(80) NULL,
                quantity_allocated FLOAT NOT NULL DEFAULT 0,
                quantity_in_progress FLOAT NOT NULL DEFAULT 0,
                quantity_in_stock FLOAT NOT NULL DEFAULT 0,
                quantity_available FLOAT NOT NULL DEFAULT 0,
                status NVARCHAR(20) NULL,
                location_type NVARCHAR(30) NULL,
                unit NVARCHAR(20) NOT NULL,
                received_date DATE NULL,
                last_receipt_date DATE NULL,
                last_issue_date DATE NULL,
                updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                FOREIGN KEY (article_id) REFERENCES articles(id)
            );
            CREATE INDEX IX_stock_lot_lines_article ON stock_lot_lines(article_id);
            CREATE INDEX IX_stock_lot_lines_lot ON stock_lot_lines(lot_code);
        END;
        """
    )


def sync_stocks_to_sim_cbn(cur: pyodbc.Cursor, stocks: dict[str, dict] | None = None) -> int:
    """Propage stock_balances vers sim_cbn_parameters (stock + reserve allouee)."""
    cur.execute(
        """
        SELECT p.simulation_id, p.article_id, sa.code, sb.quantity_available
        FROM sim_cbn_parameters p
        INNER JOIN sim_articles sa
            ON sa.id = p.article_id AND sa.simulation_id = p.simulation_id
        INNER JOIN articles a ON a.code = sa.code
        INNER JOIN stock_balances sb ON sb.article_id = a.id
        """
    )
    rows = cur.fetchall()
    updated = 0
    for sim_id, article_id, code, qty_available in rows:
        code_key = str(code).strip().upper()
        qty_alloc = float(stocks.get(code_key, {}).get("qty_alloc", 0.0)) if stocks else None
        if qty_alloc is not None:
            cur.execute(
                """
                UPDATE sim_cbn_parameters
                SET on_hand_stock = ?, reserved_quantity = ?
                WHERE simulation_id = ? AND article_id = ?
                """,
                float(qty_available),
                qty_alloc,
                int(sim_id),
                int(article_id),
            )
        else:
            cur.execute(
                """
                UPDATE sim_cbn_parameters
                SET on_hand_stock = ?
                WHERE simulation_id = ? AND article_id = ?
                """,
                float(qty_available),
                int(sim_id),
                int(article_id),
            )
        updated += cur.rowcount
    return updated


def sync_only() -> None:
    cn = pyodbc.connect(get_connection_string())
    cn.autocommit = False
    cur = cn.cursor()
    try:
        updated = sync_stocks_to_sim_cbn(cur)
        cn.commit()
        print(f"Parametres simulation synchronises : {updated}")
    except Exception:
        cn.rollback()
        raise
    finally:
        cur.close()
        cn.close()


def import_stocks(xlsx_path: Path, replace_existing: bool, sync_sim: bool = True) -> None:
    lines = load_stock_lines(xlsx_path)
    stocks = aggregate_stock_lines(lines)
    print(f"Fichier : {xlsx_path}")
    print(f"Lignes detail (lot/emplacement) : {len(lines)}")
    print(f"Articles uniques (agreges)     : {len(stocks)}")

    cn = pyodbc.connect(get_connection_string())
    cn.autocommit = False
    cur = cn.cursor()
    try:
        family_id = ensure_imported_family(cur)
        ensure_stock_lot_lines_table(cur)
        cur.execute("DELETE FROM stock_lot_lines")

        created = 0
        updated_articles = 0
        upserted_stocks = 0
        inserted_lines = 0

        cur.execute("SELECT id, code FROM articles")
        by_code = {str(r[1]).strip().upper(): int(r[0]) for r in cur.fetchall()}

        for code, data in stocks.items():
            label = (data["label"] or code)[:255]
            unit = data["unit"] or "UN"
            qty = float(data["qty_available"])

            if code in by_code:
                article_id = by_code[code]
                cur.execute(
                    """
                    UPDATE articles
                    SET label = ?,
                        default_unit = ?,
                        status = 'ACTIVE'
                    WHERE id = ?
                    """,
                    label,
                    unit,
                    article_id,
                )
                updated_articles += 1
            else:
                cur.execute(
                    """
                    INSERT INTO articles (
                        family_id, code, label, article_type, default_unit, status,
                        source_system, creation_mode
                    )
                    OUTPUT INSERTED.id
                    VALUES (?, ?, ?, 'COMPONENT', ?, 'ACTIVE', 'STOCK_IMPORT', 'IMPORTED')
                    """,
                    family_id,
                    code,
                    label,
                    unit,
                )
                article_id = int(cur.fetchone()[0])
                by_code[code] = article_id
                created += 1

            cur.execute(
                """
                MERGE stock_balances AS target
                USING (SELECT ? AS article_id, ? AS quantity_available, ? AS unit) AS source
                ON target.article_id = source.article_id
                WHEN MATCHED THEN UPDATE SET
                    quantity_available = source.quantity_available,
                    unit = source.unit,
                    updated_at = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN INSERT (article_id, quantity_available, unit)
                    VALUES (source.article_id, source.quantity_available, source.unit);
                """,
                article_id,
                qty,
                unit,
            )
            upserted_stocks += 1

        for line in lines:
            article_id = by_code[line["code"]]
            cur.execute(
                """
                INSERT INTO stock_lot_lines (
                    article_id, location_code, warehouse, lot_code, supplier_lot,
                    quantity_allocated, quantity_in_progress, quantity_in_stock, quantity_available,
                    status, location_type, unit,
                    received_date, last_receipt_date, last_issue_date
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                article_id,
                line["location_code"],
                line["warehouse"],
                line["lot_code"],
                line["supplier_lot"],
                float(line["quantity_allocated"]),
                float(line["quantity_in_progress"]),
                float(line["quantity_in_stock"]),
                float(line["quantity_available"]),
                line["status"],
                line["location_type"],
                line["unit"],
                line["received_date"],
                line["last_receipt_date"],
                line["last_issue_date"],
            )
            inserted_lines += 1

        sim_synced = 0
        if sync_sim:
            sim_synced = sync_stocks_to_sim_cbn(cur, stocks)

        cn.commit()
        print(f"Articles crees         : {created}")
        print(f"Articles mis a jour    : {updated_articles}")
        print(f"Stocks upserts         : {upserted_stocks}")
        print(f"Lignes lot importees   : {inserted_lines}")
        if sync_sim:
            print(f"Sim CBN synchronises   : {sim_synced}")

        cur.execute("SELECT COUNT(*) FROM stock_lot_lines")
        print(f"Total stock_lot_lines  : {cur.fetchone()[0]}")
        cur.execute("SELECT COUNT(*) FROM stock_balances")
        print(f"Total stock_balances   : {cur.fetchone()[0]}")
    except Exception:
        cn.rollback()
        raise
    finally:
        cur.close()
        cn.close()


def main() -> None:
    parser = argparse.ArgumentParser(description="Import Excel stocks vers AxioplanMvp")
    parser.add_argument(
        "xlsx",
        nargs="?",
        default=r"c:\Users\USER\Downloads\liste_stocks.xlsx",
        help="Chemin vers liste_stocks.xlsx",
    )
    parser.add_argument(
        "--replace-existing",
        action="store_true",
        help="Reserve (comportement actuel = upsert).",
    )
    parser.add_argument(
        "--sync-only",
        action="store_true",
        help="Synchronise uniquement sim_cbn_parameters depuis stock_balances.",
    )
    parser.add_argument(
        "--no-sync-sim",
        action="store_true",
        help="N'actualise pas sim_cbn_parameters apres import.",
    )
    args = parser.parse_args()
    if args.sync_only:
        sync_only()
        return
    path = Path(args.xlsx)
    if not path.exists():
        print(f"Fichier introuvable : {path}", file=sys.stderr)
        sys.exit(1)
    import_stocks(path, args.replace_existing, sync_sim=not args.no_sync_sim)


if __name__ == "__main__":
    main()
