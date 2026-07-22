from __future__ import annotations

import os
from pathlib import Path

import pyodbc


DEFAULT_CONNECTION_STRING = (
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost\\SQLEXPRESS;Database=AxioplanMvp;"
    "Trusted_Connection=yes;TrustServerCertificate=yes;"
)


def get_connection_string() -> str:
    return os.environ.get("AXIOPLAN_CONNECTION_STRING", DEFAULT_CONNECTION_STRING)


def connect(connection_string: str | None = None) -> pyodbc.Connection:
    connection = pyodbc.connect(connection_string or get_connection_string())
    connection.autocommit = False
    return connection


def execute_script(connection: pyodbc.Connection, script_path: str | Path) -> None:
    script = Path(script_path).read_text(encoding="utf-8")
    cursor = connection.cursor()
    for batch in _split_sql_batches(script):
        if batch.strip():
            cursor.execute(batch)
    connection.commit()


def fetch_scalar(connection: pyodbc.Connection, query: str, params: tuple = ()) -> object | None:
    row = connection.execute(query, params).fetchone()
    return row[0] if row else None


def _split_sql_batches(script: str) -> list[str]:
    batches: list[str] = []
    current: list[str] = []
    for line in script.splitlines():
        if line.strip().upper() == "GO":
            batches.append("\n".join(current))
            current = []
        else:
            current.append(line)
    if current:
        batches.append("\n".join(current))
    return batches
