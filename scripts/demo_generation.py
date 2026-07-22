"""Console demo — Configurateur Gammes & Nomenclatures (MVP local)."""

from __future__ import annotations

import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT_DIR))
sys.path.insert(0, str(ROOT_DIR / "src"))

from axioplan.core.database import connect, get_connection_string
from axioplan.modules.gammes_nomenclatures.generation import (
    build_size_color_variants,
    calculate_gross_quantity,
    calculate_net_quantity,
    calculate_operation_time,
    profile_can_generate,
)
from scripts.init_db import DEFAULT_DATABASE, main as init_database

FAMILY_CODE = "PULL_COL_ROND"
PROFILE_CODE = "PROFILE_SIZE_COLOR"
SIZES = ["S", "M", "L"]
COLORS = ["MINT", "NOIR"]
SECTION_WIDTH = 72


@dataclass(frozen=True)
class BomLine:
    line_no: int
    component_code: str
    component_label: str
    quantity_base: float
    unit: str
    loss_rate: float
    behavior: str


@dataclass(frozen=True)
class RoutingOperation:
    operation_no: int
    name: str
    workcenter_code: str
    time_base: float
    time_unit: str
    behavior: str


@dataclass(frozen=True)
class CalculationTrace:
    object_type: str
    variant_code: str
    rule_code: str
    source_label: str
    base_value: float
    size_coefficient: float
    color_coefficient: float
    loss_rate: float
    result_value: float
    unit: str
    formula: str


def section(title: str) -> None:
    print()
    print("=" * SECTION_WIDTH)
    print(title)
    print("=" * SECTION_WIDTH)


def subsection(title: str) -> None:
    print()
    print(f"--- {title} ---")


def load_coefficient(
    connection,
    attribute_code: str,
    option_code: str,
    table: str,
) -> float:
    row = connection.execute(
        f"""
        SELECT cc.coefficient
        FROM {table} cc
        JOIN product_families pf ON pf.id = cc.product_family_id
        JOIN attribute_definitions ad ON ad.id = cc.attribute_id
        JOIN attribute_options ao ON ao.id = cc.option_id
        WHERE pf.code = ?
          AND ad.code = ?
          AND ao.technical_code = ?
          AND cc.status = 'VALIDATED'
        """,
        (FAMILY_CODE, attribute_code, option_code),
    ).fetchone()
    return float(row[0]) if row else 1.0


def load_family(connection):
    return connection.execute(
        """
        SELECT pf.code, pf.label, bb.code AS bom_code, rb.code AS routing_code
        FROM product_families pf
        LEFT JOIN bom_bases bb ON bb.product_family_id = pf.id
        LEFT JOIN routing_bases rb ON rb.product_family_id = pf.id
        WHERE pf.code = ?
        """,
        (FAMILY_CODE,),
    ).fetchone()


def load_profile(connection):
    return connection.execute(
        """
        SELECT code, label, status, can_generate_bom, can_generate_routing
        FROM generation_profiles
        WHERE code = ?
        """,
        (PROFILE_CODE,),
    ).fetchone()


def load_base_article_code(connection) -> str:
    row = connection.execute(
        """
        SELECT TOP 1 a.code
        FROM articles a
        JOIN article_families af ON af.id = a.family_id
        JOIN product_families pf ON pf.article_family_id = af.id
        WHERE pf.code = ? AND a.article_type = 'FINISHED_GOOD'
        """,
        (FAMILY_CODE,),
    ).fetchone()
    return row[0] if row else "AH25PLUMIERE"


def load_bom_lines(connection) -> list[BomLine]:
    rows = connection.execute(
        """
        SELECT bl.line_no, a.code AS component_code, a.label AS component_label,
               bl.quantity_base, bl.unit, bl.loss_rate, bl.behavior
        FROM bom_base_lines bl
        JOIN bom_bases b ON b.id = bl.bom_base_id
        JOIN product_families pf ON pf.id = b.product_family_id
        JOIN articles a ON a.id = bl.component_article_id
        WHERE pf.code = ?
        ORDER BY bl.line_no
        """,
        (FAMILY_CODE,),
    ).fetchall()
    return [
        BomLine(
            line_no=row[0],
            component_code=row[1],
            component_label=row[2],
            quantity_base=float(row[3]),
            unit=row[4],
            loss_rate=float(row[5]),
            behavior=row[6],
        )
        for row in rows
    ]


def load_routing_operations(connection) -> list[RoutingOperation]:
    rows = connection.execute(
        """
        SELECT ro.operation_no, ro.name, wc.code AS workcenter_code,
               ro.time_base, ro.time_unit, ro.behavior
        FROM routing_base_operations ro
        JOIN routing_bases rb ON rb.id = ro.routing_base_id
        JOIN product_families pf ON pf.id = rb.product_family_id
        JOIN workcenters wc ON wc.id = ro.workcenter_id
        WHERE pf.code = ?
        ORDER BY ro.operation_no
        """,
        (FAMILY_CODE,),
    ).fetchall()
    return [
        RoutingOperation(
            operation_no=row[0],
            name=row[1],
            workcenter_code=row[2],
            time_base=float(row[3]),
            time_unit=row[4],
            behavior=row[5],
        )
        for row in rows
    ]


def compute_bom_line(
    line: BomLine,
    size_coefficient: float,
    color_coefficient: float,
) -> tuple[float, float, CalculationTrace | None]:
    if line.behavior == "CALCULATED":
        net = calculate_net_quantity(line.quantity_base, size_coefficient, color_coefficient)
        gross = calculate_gross_quantity(net, line.loss_rate)
        trace = CalculationTrace(
            object_type="BOM_LINE",
            variant_code="",
            rule_code=f"BOM_LINE_MVP_{line.line_no}",
            source_label=f"{line.component_code} (ligne {line.line_no})",
            base_value=line.quantity_base,
            size_coefficient=size_coefficient,
            color_coefficient=color_coefficient,
            loss_rate=line.loss_rate,
            result_value=gross,
            unit=line.unit,
            formula="base * size * color / (1 - loss)",
        )
        return net, gross, trace

    net = line.quantity_base
    gross = calculate_gross_quantity(net, line.loss_rate) if line.loss_rate else net
    return net, gross, None


def run_demo() -> None:
    try:
        connection = connect()
        connection.execute("SELECT COUNT(*) FROM product_families").fetchone()
    except Exception:
        print("Base SQL Server absente : initialisation en cours...")
        init_database()
        connection = connect()

    try:
        family = load_family(connection)
        profile = load_profile(connection)
        if not family or not profile:
            raise SystemExit("Donnees de demonstration introuvables. Lancez : python scripts/init_db.py")

        base_article = load_base_article_code(connection)
        bom_lines = load_bom_lines(connection)
        routing_ops = load_routing_operations(connection)
        variants = build_size_color_variants(base_article, SIZES, COLORS)

        section("AXIOPLAN - Demo Configurateur Gammes & Nomenclatures")
        print("Mode : SIMULATION (aucun objet definitif CBN)")
        print(f"Base : {DEFAULT_DATABASE} ({get_connection_string()})")

        section("1. Famille produit fini")
        print(f"Code      : {family[0]}")
        print(f"Libelle   : {family[1]}")
        print(f"BOM base  : {family[2]}")
        print(f"Gamme base: {family[3]}")
        print(f"Article   : {base_article}")

        section("2. Profil de generation")
        print(f"Code      : {profile[0]}")
        print(f"Libelle   : {profile[1]}")
        print(f"Statut    : {profile[2]}")
        print(f"Dimensions: Taille x Couleur")
        print(f"BOM       : {'oui' if profile[3] else 'non'}")
        print(f"Gamme     : {'oui' if profile[4] else 'non'}")
        if not profile_can_generate(profile[2]):
            print("\nATTENTION : profil non valide — simulation seulement.")
        else:
            print("\nProfil valide : generation exploitable possible apres validation metier.")

        section("3. Simulation demandee")
        print(f"Tailles   : {', '.join(SIZES)}")
        print(f"Couleurs  : {', '.join(COLORS)}")
        print(f"Variantes : {len(variants)} combinaisons")

        section("4. Variantes generees")
        print(f"{'#':<3} {'Code article':<30} {'Taille':<8} {'Couleur'}")
        print("-" * SECTION_WIDTH)
        for index, variant in enumerate(variants, start=1):
            print(f"{index:<3} {variant.article_code:<30} {variant.size_code:<8} {variant.color_code}")

        section("5. Resume des besoins composants (BOM)")
        header = f"{'Variante':<28} {'Composant':<12} {'Net':>8} {'Brut':>8} {'Unite'}"
        print(header)
        print("-" * SECTION_WIDTH)

        all_traces: list[CalculationTrace] = []
        for variant in variants:
            size_coeff = load_coefficient(connection, "SIZE", variant.size_code, "consumption_coefficients")
            color_coeff = load_coefficient(connection, "COLOR", variant.color_code, "consumption_coefficients")

            for line in bom_lines:
                net, gross, trace = compute_bom_line(line, size_coeff, color_coeff)
                short_code = variant.article_code.replace(f"{base_article}-", "")
                print(
                    f"{short_code:<28} {line.component_code:<12} {net:>8.3f} {gross:>8.3f} {line.unit}"
                )
                if trace:
                    all_traces.append(
                        CalculationTrace(
                            **{**trace.__dict__, "variant_code": variant.article_code}
                        )
                    )

        subsection("Totaux par composant (6 variantes)")
        totals: dict[str, dict[str, float]] = defaultdict(lambda: {"net": 0.0, "gross": 0.0, "unit": ""})
        for variant in variants:
            size_coeff = load_coefficient(connection, "SIZE", variant.size_code, "consumption_coefficients")
            color_coeff = load_coefficient(connection, "COLOR", variant.color_code, "consumption_coefficients")
            for line in bom_lines:
                net, gross, _ = compute_bom_line(line, size_coeff, color_coeff)
                totals[line.component_code]["net"] += net
                totals[line.component_code]["gross"] += gross
                totals[line.component_code]["unit"] = line.unit

        for component_code, values in totals.items():
            print(
                f"  {component_code:<12} net={values['net']:>7.3f}  "
                f"brut={values['gross']:>7.3f}  {values['unit']}"
            )

        section("6. Resume des temps / gamme")
        print(f"Operations de base chargees : {len(routing_ops)}")
        print(f"Unite temps                 : {routing_ops[0].time_unit if routing_ops else 'N/A'} (a confirmer metier)")
        print()
        print(f"{'Variante':<12} {'Temps total':>12} {'Principaux workcenters'}")
        print("-" * SECTION_WIDTH)

        for variant in variants:
            size_coeff = load_coefficient(connection, "SIZE", variant.size_code, "time_coefficients")
            color_coeff = load_coefficient(connection, "COLOR", variant.color_code, "time_coefficients")

            by_workcenter: dict[str, float] = defaultdict(float)
            total_time = 0.0
            for op in routing_ops:
                generated_time = calculate_operation_time(op.time_base, size_coeff, color_coeff)
                by_workcenter[op.workcenter_code] += generated_time
                total_time += generated_time

                if op.operation_no == 110 and len(all_traces) < 20:
                    all_traces.append(
                        CalculationTrace(
                            object_type="ROUTING_OPERATION",
                            variant_code=variant.article_code,
                            rule_code="ROUTING_COPY_TO_VALIDATE_MVP",
                            source_label=f"{op.name} (op. {op.operation_no})",
                            base_value=op.time_base,
                            size_coefficient=size_coeff,
                            color_coefficient=color_coeff,
                            loss_rate=0.0,
                            result_value=generated_time,
                            unit=op.time_unit,
                            formula="base * size * color",
                        )
                    )

            top_centers = sorted(by_workcenter.items(), key=lambda item: item[1], reverse=True)[:3]
            centers_summary = ", ".join(f"{code}={time:.1f}" for code, time in top_centers)
            short_code = variant.article_code.replace(f"{base_article}-", "")
            print(f"{short_code:<12} {total_time:>12.1f}  {centers_summary}")

        subsection("Exemple : 3 premieres operations (variante L / NOIR)")
        example = next(v for v in variants if v.size_code == "L" and v.color_code == "NOIR")
        size_coeff = load_coefficient(connection, "SIZE", "L", "time_coefficients")
        color_coeff = load_coefficient(connection, "COLOR", "NOIR", "time_coefficients")
        print(f"{'Op.':<5} {'Operation':<22} {'Workcenter':<12} {'Base':>7} {'Genere':>7}")
        print("-" * SECTION_WIDTH)
        for op in routing_ops[:3]:
            generated = calculate_operation_time(op.time_base, size_coeff, color_coeff)
            print(
                f"{op.operation_no:<5} {op.name:<22} {op.workcenter_code:<12} "
                f"{op.time_base:>7.1f} {generated:>7.1f}"
            )
        print(f"\nVariante exemple : {example.article_code}")

        section("7. Traces de calcul (simulees)")
        print("Traces representant l'explication des resultats (formules MVP).")
        print()
        print(
            f"{'Type':<18} {'Variante':<22} {'Source':<28} {'Resultat':>8} {'Formule'}"
        )
        print("-" * SECTION_WIDTH)

        shown_variants = {f"{base_article}-L-NOIR", f"{base_article}-S-MINT"}
        for trace in all_traces:
            if trace.variant_code not in shown_variants and trace.object_type == "ROUTING_OPERATION":
                continue
            if trace.object_type == "BOM_LINE" and trace.variant_code != f"{base_article}-L-NOIR":
                continue
            short_variant = trace.variant_code.replace(f"{base_article}-", "")
            print(
                f"{trace.object_type:<18} {short_variant:<22} {trace.source_label:<28} "
                f"{trace.result_value:>7.3f} {trace.unit:<4} {trace.formula}"
            )
            print(
                f"{'':18} base={trace.base_value}  coeff_taille={trace.size_coefficient}  "
                f"coeff_couleur={trace.color_coefficient}  perte={trace.loss_rate}"
            )

        section("Fin de la demonstration")
        print("Donnees confirmees : operations et workcenters issus du fichier prod.")
        print("Donnees simulees   : BOM exemple, composants FIL-MINT / VCOMP-STD / SACHET-STD,")
        print("                     coefficients taille/couleur de demonstration.")
        print()
        print("Pour reinitialiser la base : python scripts/init_db.py")
        print("Pour relancer la demo     : python scripts/demo_generation.py")

    finally:
        connection.close()


if __name__ == "__main__":
    run_demo()
