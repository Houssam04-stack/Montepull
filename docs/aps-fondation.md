# APS Axioplan — fondation (journal, attendus, referentiel, compilateur)

## Role

Briques structurantes v1 conformes a l'ordre du cahier des charges APS :

1. Journal des faits (M7) — append-only
2. Journal des attendus (M8) — immuables
3. Referentiel APS minimal (additif)
4. Squelette compilateur (artefacts BESOINS / CHARGES / HRE / DELAI)

## Tables nouvelles (`database/aps_schema.sql`)

- `aps_journal_events`
- `aps_expectations`
- `aps_places`, `aps_calendars`, `aps_work_regimes`, `aps_teams`
- `aps_charge_centers`, `aps_charge_posts`, `aps_post_regime_assignments`
- `aps_stock_zones`, `aps_stock_lots`
- `aps_circuits`, `aps_external_engagements`, `aps_supplier_lead_times`
- `aps_compiled_artifacts`, `aps_compile_source_index`

## Colonnes heritees (additives, non destructives)

| Table | Colonnes | Note |
|-------|----------|------|
| `articles` | `aps_decoupling_point`, `aps_traceability`, `aps_hre_precompile` | `hre` derive compilateur |
| `customers` | `aps_bath_compat_rule`, `aps_usual_engagement_month` | TO_CONFIRM si vide |
| `bom_base_lines` | `aps_operation_code`, `aps_bath_constraint` | lien operation CDC |
| `bom_bases` | `aps_valid_from`, `aps_valid_to` | validite nomenclature |

`stock_balances` (agregat legacy) **conserve**. Stocks lot/bain = `aps_stock_lots`.

## UI

- `/aps/journal`
- `/aps/attendus`
- `/aps/referentiel`
- `/aps/compilateur`

## Non branche

Le CBN / pegging / simulation existants **ne consomment pas** encore le compilateur.
Adaptation future via `ApsCompilerService` + artefacts VALID uniquement.

## Lancement schema

```powershell
sqlcmd -S localhost\SQLEXPRESS -E -C -d AxioplanMvp -i database\aps_schema.sql
# ou automatique via EnsureSchema au premier appel APS
```
