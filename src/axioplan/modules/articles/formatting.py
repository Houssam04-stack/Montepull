"""Formatage et normalisation des valeurs attribut (cadrage article §7.2)."""

from __future__ import annotations

import re
import unicodedata


def _remove_diacritics(value: str) -> str:
    normalized = unicodedata.normalize("NFD", value)
    return "".join(c for c in normalized if unicodedata.category(c) != "Mn")


def format_attribute_value(
    raw_value: str,
    *,
    trim_spaces: bool = True,
    collapse_spaces: bool = True,
    remove_internal_spaces: bool = False,
    case_rule: str = "UPPER",
    strip_accents_for_code: bool = True,
    forbidden_chars: str | None = None,
) -> dict[str, str]:
    working = raw_value or ""
    if trim_spaces:
        working = working.strip()
    if collapse_spaces:
        working = re.sub(r"\s+", " ", working)
    if remove_internal_spaces:
        working = working.replace(" ", "")
    if forbidden_chars:
        for char in forbidden_chars:
            working = working.replace(char, "")

    if case_rule == "UPPER":
        normalized = working.upper()
        display = working.title() if not remove_internal_spaces else normalized
    elif case_rule == "LOWER":
        normalized = working.lower()
        display = normalized
    elif case_rule == "TITLE":
        normalized = working.title()
        display = normalized
    else:
        normalized = working
        display = working

    technical_base = _remove_diacritics(normalized) if strip_accents_for_code else normalized
    technical_base = technical_base.upper()
    technical_code = technical_base if remove_internal_spaces else technical_base.replace(" ", "-")

    return {
        "raw_value": raw_value,
        "display_value": display,
        "normalized_value": normalized,
        "technical_code": technical_code,
    }
