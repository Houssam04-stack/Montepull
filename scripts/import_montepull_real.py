"""Importe les fichiers Excel Montepull reels (staging -> validate -> promote)."""
from __future__ import annotations

import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

# Utilise le repository via un petit programme C# one-shot serait plus fiable.
# Ici on lance `dotnet run` sur un outil si present, sinon on documente.

DOWNLOADS = Path(os.environ.get("USERPROFILE", "")) / "Downloads"

FILES = [
    DOWNLOADS / "commandes.xlsx",
    DOWNLOADS / "ListeSuivi_2026-07-22.xlsx",
    DOWNLOADS / "SuiviOperations_2026-07-22.xlsx",
    DOWNLOADS / "DOUBLYGILF.xlsx",
    DOWNLOADS / "Nomenclature DOULBYGILF.xlsx",
]


def main() -> None:
    missing = [p for p in FILES if not p.exists()]
    if missing:
        print("Fichiers manquants :")
        for p in missing:
            print(f"  - {p}")
        # fallback nomenclature .xls
        xls = DOWNLOADS / "Nomenclature DOULBYGILF.xls"
        if xls.exists():
            FILES[-1] = xls
            missing = [p for p in FILES if not p.exists()]
        if missing:
            sys.exit(1)

    tool = ROOT / "scripts" / "run_montepull_import.csx"
    print("Lancement import via outil .NET...")
    args = " ".join(f'"{p}"' for p in FILES)
    cmd = f'dotnet script "{tool}" {args}' if tool.exists() else None
    if cmd:
        os.system(cmd)
        return

    # Fallback: compile & run dedicated console helper
    helper = ROOT / "scripts" / "MontepullImportRunner"
    if (helper / "MontepullImportRunner.csproj").exists():
        file_args = " ".join(f'"{p}"' for p in FILES)
        code = os.system(f'dotnet run --project "{helper}" -- {file_args}')
        sys.exit(code)

    print("Outil d'import introuvable. Utilisez l'UI /imports/montepull ou le runner C#.")
    for p in FILES:
        print(f"  OK {p.name} ({p.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
