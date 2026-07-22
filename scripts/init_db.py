from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[1]
DEFAULT_SERVER = r"localhost\SQLEXPRESS"
DEFAULT_DATABASE = "AxioplanMvp"
DEFAULT_CONNECTION_STRING = (
    "Driver={ODBC Driver 18 for SQL Server};"
    f"Server={DEFAULT_SERVER};Database={DEFAULT_DATABASE};"
    "Trusted_Connection=yes;TrustServerCertificate=yes;"
)


def get_connection_string() -> str:
    return os.environ.get("AXIOPLAN_CONNECTION_STRING", DEFAULT_CONNECTION_STRING)


def run_sqlcmd(script_path: Path, database: str | None = None) -> None:
    server = os.environ.get("AXIOPLAN_SQL_SERVER", DEFAULT_SERVER)
    command = [
        "sqlcmd",
        "-S",
        server,
        "-E",
        "-C",
        "-b",
        "-i",
        str(script_path),
    ]
    if database:
        command.extend(["-d", database])

    result = subprocess.run(command, capture_output=True, text=True, check=False)
    if result.returncode != 0:
        raise RuntimeError(
            f"Echec sqlcmd ({script_path.name}) :\n{result.stdout}\n{result.stderr}"
        )


def main() -> None:
    reset = "--reset" in sys.argv
    schema_path = ROOT_DIR / "database" / "schema.sql"
    migrate_path = ROOT_DIR / "database" / "migrate.sql"
    seed_referentials = ROOT_DIR / "database" / "seed" / "001_referentials.sql"
    seed_prod = ROOT_DIR / "database" / "seed" / "002_prod_example.sql"
    seed_articles = ROOT_DIR / "database" / "seed" / "003_article_configurator.sql"
    seed_doublygilf = ROOT_DIR / "database" / "seed" / "004_doublygilf.sql"
    seed_parameters = ROOT_DIR / "database" / "seed" / "005_parameters.sql"
    seed_multilevel = ROOT_DIR / "database" / "seed" / "006_multilevel_bom.sql"
    seed_pegging = ROOT_DIR / "database" / "seed" / "007_pegging.sql"
    seed_formula = ROOT_DIR / "database" / "seed" / "008_formula_configurator.sql"
    sim_schema = ROOT_DIR / "database" / "sim_cbn_schema.sql"
    aps_schema = ROOT_DIR / "database" / "aps_schema.sql"

    for script_path in (
        schema_path,
        migrate_path,
        seed_referentials,
        seed_prod,
        seed_articles,
        seed_doublygilf,
        seed_parameters,
        seed_multilevel,
        seed_pegging,
        seed_formula,
        sim_schema,
        aps_schema,
    ):
        if not script_path.exists():
            raise FileNotFoundError(f"Script SQL introuvable : {script_path}")

    print(f"Initialisation SQL Server : {DEFAULT_SERVER} / {DEFAULT_DATABASE}")
    if reset:
        print("Mode --reset : suppression et recreation complete de la base.")
        run_sqlcmd(schema_path)
        run_sqlcmd(seed_referentials, DEFAULT_DATABASE)
        run_sqlcmd(seed_prod, DEFAULT_DATABASE)
        run_sqlcmd(seed_articles, DEFAULT_DATABASE)
        run_sqlcmd(seed_doublygilf, DEFAULT_DATABASE)
        run_sqlcmd(seed_parameters, DEFAULT_DATABASE)
        run_sqlcmd(seed_multilevel, DEFAULT_DATABASE)
        run_sqlcmd(seed_pegging, DEFAULT_DATABASE)
        run_sqlcmd(seed_formula, DEFAULT_DATABASE)
        run_sqlcmd(sim_schema, DEFAULT_DATABASE)
        run_sqlcmd(aps_schema, DEFAULT_DATABASE)
    else:
        print("Mode migration : les donnees existantes sont conservees.")
        run_sqlcmd(migrate_path)
        run_sqlcmd(seed_formula, DEFAULT_DATABASE)
        run_sqlcmd(aps_schema, DEFAULT_DATABASE)
        # Schema sim_* : recree seulement si absent (script drop+create — a eviter hors --reset)
        # L'app cree les tables a la volee via EnsureSchemaAsync.

    print(f"Base SQL Server initialisee : {DEFAULT_DATABASE}")
    print(f"Chaine de connexion : {get_connection_string()}")
    if not reset:
        print("Pour repartir de zero : python scripts/init_db.py --reset")


if __name__ == "__main__":
    try:
        main()
    except FileNotFoundError as error:
        print(error, file=sys.stderr)
        print(
            "Installez sqlcmd (SQL Server tools) ou definissez AXIOPLAN_SQL_SERVER.",
            file=sys.stderr,
        )
        sys.exit(1)
    except RuntimeError as error:
        print(error, file=sys.stderr)
        sys.exit(1)
