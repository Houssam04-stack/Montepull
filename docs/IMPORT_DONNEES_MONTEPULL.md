# Import données Montepull (réelles)

Document de mapping et de règles pour l’import des fichiers Excel Montepull vers Axioplan (staging puis promotion métier).

Dataset actif : `MONTEPULL_REAL` (fallback `DEMO`). Configuration : `Dataset:Active` dans `appsettings.json`.

## 1. Fichiers analysés

Analyse complète réalisée le **2026-07-22 / 2026-07-23** (parcours de toutes les lignes, pas un échantillon).

| Fichier | Rôle | Feuilles | Volume | Clés métier |
|---------|------|----------|--------|-------------|
| `commandes.xlsx` | Demandes clients / avancement | `Commandes` | 5 lignes ; 1 commande `SO26000321` ; 5 articles `PE260112`…`PE260116` ; client `CL00011` | Commande + Article |
| `ListeSuivi_2026-07-22.xlsx` | Suivi atelier (OF, paquets, route) | `Détails` (249), `Par Opération` (86), `Feuille Route` (64) | 64 OF ; 19 CMD ; 54 articles ; ops **10,30,40,50,60,70** (pas d’op 20 ce jour) | OF ; événement = OF+op+paquet+dates+qté+ligne |
| `SuiviOperations_2026-07-22.xlsx` | Historique de réalisation (scans) | `Détails` (72 918), `Par OF` (7 527) | 2 719 OF ; 1 570 articles ; ops **10,20,30,35,40,50,60,70** | Idem événement (source HIST) |
| `DOUBLYGILF.xlsx` | Besoin d’achat / grille tailles / fournitures | `Marwa simple Standard`, `Fiche de prix` (vide) | Zone métier L6–L32 ; 63 formules dans les 80 premières lignes | Parent `DOUBLYGILF` ; composants fournitures |
| `Nomenclature DOULBYGILF.xls` (+ `.xlsx`) | Fiche nomenclature fournitures | `Feuil3` | En-tête libre L2–L12 ; table fournitures L15–L34 | Parent `DOULBYGILF` (alias `DOUBLYGILF`) |

Orthographe : `DOUBLYGILF` (besoin) vs `DOULBYGILF` (nomenclature) — **hypothèse** : même référence produit, alias appliqué à la promotion.

### 1.1 Anomalies quantitatives (fichiers source)

| Source | Observation |
|--------|-------------|
| commandes | 0 doublon (Commande+Article) ; 0 qté négative ; **toutes lignes non lancées** (`Qte_Lancee=0`) |
| ListeSuivi Détails | 0 ligne vide ; 0 qté négative ; 0 OF multi-articles ; dates souvent à **1–2 s** (scan) |
| SuiviOperations | ~69 000 durées SCAN (&lt;30 s) ; ~3 806 PROCESS ; **3** Fin&lt;Début ; **109** dates non parsables ; **4** qté négatives ; op **35** (65–64 obs.) hors mapping standard 10–70 |
| DOUBLYGILF | Totaux `=SUM(...)` / `=G12*G13` = **résultats calculés**, non stockés comme sources ; légende / coûts = visuel |
| Nomenclature | Cellules `#VALUE!` Excel → non importées comme nombres ; `Fiche de prix` vide |

## 2. Mapping fichier → entités SQL

| Source | Staging | Métier (après acceptation) |
|--------|---------|----------------------------|
| commandes | `mp_staging_commandes` | `customers`, `articles`, `sales_orders`, `sales_order_lines` (+ colonnes avancement) |
| ListeSuivi Détails | `mp_staging_suivi_ops` (`LISTE`) | `manufacturing_orders`, `mo_production_events`, liens commande |
| ListeSuivi Par Opération | contrôle | `mp_of_operation_agg` (recalculé aussi depuis Détails) |
| ListeSuivi Feuille Route | `mp_staging_gamme` (ops 10–70) | `manufacturing_order_lines` + contrôle cohérence |
| SuiviOperations | `mp_staging_suivi_ops` (`HIST`) | événements + `mp_duration_stats` |
| DOUBLYGILF | `mp_staging_nomenclature` / params | `articles`, `bom_bases`, `bom_base_lines` |
| Nomenclature DOULBYGILF | `mp_staging_nomenclature` | idem composants |

Infrastructure : `mp_import_batches`, `mp_import_files`, `mp_import_rows`, `mp_import_anomalies`, `mp_import_mappings`, `mp_dataset_config`.

## 3. Mapping colonnes → champs

### 3.1 commandes.xlsx / Commandes

| Colonne Excel | Champ staging / métier | Notes |
|---------------|------------------------|-------|
| Commande | `order_code` / `sales_orders.code` | Clé commande |
| Client | `customer_code` | Code client |
| Article | `article_code` / `articles.code` | Clé article |
| Designation | `designation` / `articles.label` | Ne jamais fusionner uniquement dessus |
| Qte_Commandee | `qty_ordered` / `sales_order_lines.quantity` | |
| Qte_Lancee | `qty_launched` | |
| Somme_Operations | `somme_operations` | **Pas** assimilée automatiquement à une quantité produite |
| Somme_Op_70_* | conservé staging | Indicateur op. 70 |
| Derniere_Operation | `last_operation` | |
| Qte_Fabriquee_Derniere_Operation | `qty_produced` | Quantité fabriquée retenue |

Clé métier ligne : `Commande + Article`.

Règles :

- `Reste à lancer = max(Qté commandée − Qté lancée, 0)`
- `Reste à fabriquer = max(Qté commandée − Qté fabriquée valide, 0)`
- Non lancé : `Qte_Lancee = 0`
- Partiellement lancé : `0 < Qte_Lancee < Qte_Commandee`
- Totalement lancé / terminé : selon reste à lancer / fabriquer

### 3.2 ListeSuivi — Détails

| Colonne | Champ |
|---------|-------|
| N° OF | `of_code` → `manufacturing_orders.code` |
| N° CMD | `order_code` |
| Article / Désignation | article |
| Opération | `operation_no` |
| Date Début / Date Fin | `actual_start` / `actual_end` |
| Quantité Réelle / Rejet | `qty_good` / `qty_rejected` |
| Qte OF | `of_quantity` |
| CMD Date / Livraison / Client | dates & client commande |
| Atelier / Statut / Npaquet / Date Opération | atelier, statut, paquet, horodatage |

Clé événement : `OF + opération + paquet + date début + date fin + quantité + ligne source` (hash).

**Ne pas** dédupliquer uniquement sur OF + opération (paquets / scans partiels).

Agrégat `OF + opération` : qté réalisée, rejet, 1ère début, dernière fin, nb paquets, statut, qté restante.

### 3.3 SuiviOperations

Même logique événementielle ; `Par OF` = agrégat de contrôle. Dates souvent stockées en **texte** `yyyy-MM-dd HH:mm:ss`.

Durée = Fin − Début **après** contrôles (Fin ≥ Début, non aberrante). Durées &lt; 30 s = `SCAN` (validation), pas temps industriel → exclues des stats de recalibrage. `PROCESS` = durée exploitable pour statistiques.

Stats robustes (hors anomalies) : n, moyenne, médiane, min, max, p25, p75, écart-type. **Ne remplacent pas** automatiquement les temps standards validés.

### 3.4 DOUBLYGILF / Nomenclature

| Zone | Nature | Traitement |
|------|--------|------------|
| G4 / D7 référence, C7 client, E7 jauge, F7 version | Paramètres métier | Staging / article parent |
| G12:K12 totaux commande, G14:L14 besoin fil (formules) | **Calculé Excel** | Non stocké comme source ; besoin fil source = coeffs F15 × poids si présents |
| B21:L32 fournitures | Sources (prix unitaires F, quantités) | `BomBaseLine` composants |
| Légende M3–M6, coûts P/Q | Visuel / dérivé | Ignoré |
| Nomenclature L15–L34 | Sources fournitures | `BomBaseLine` ; `#VALUE!` → anomalie, pas de nombre |

### 3.5 Opérations 10…70 (…300)

`mp_import_mappings` : code source → opération Axioplan → workcenter (nullable) → séquence → unité → nature.

Ops inconnues (ex. **35**) → anomalie `UNKNOWN_OPERATION` + staging conservé ; **pas** d’invention silencieuse de workcenter.

Référence gamme prod seed : ops 10–300 (`GAM_BASE_PULL_COL_ROND_PROD_EXEMPLE`).

## 4. Nettoyage / déduplication / idempotence

- Trim codes ; normaliser en-têtes (accents, casse).
- Dates invalides / Fin &lt; Début / qté négative → anomalie, ligne staging `REJECTED` ou `WARNING`.
- Réimport même `file_hash` : pas de doublons métier (upsert par clé métier ; événements uniques par `row_hash` global sur `mo_production_events`).
- Annulation logique de batch : `status = CANCELLED` ; `is_active = 0` sur OF / événements du batch.

## 5. Distinction réel / planifié

| Concept | Champs |
|---------|--------|
| Planifié | `PlannedStart`, `PlannedEnd`, `PlannedQuantity` |
| Réel | `ActualStart`, `ActualEnd`, `ActualGoodQuantity`, `ActualRejectedQuantity` |
| Reste | `RemainingQuantity` |
| Provenance | `DataSource` (`DEMO` / `MONTEPULL_REAL`), `ImportBatchId` |

L’UI d’import et le Gantt doivent afficher clairement **Réel** vs **Planifié** / Estimé / Simulé.

## 6. Données non exploitables / hypothèses

- Capacités atelier / TRS / stocks absents des fichiers → APS capacité reste sur référentiel existant.
- Temps standards : stats import = **proposition de recalibrage uniquement**.
- `Somme_Op_70_*` : indicateur, pas stock métier principal.
- Dataset `DEMO` conservé pour tests ; seeds non supprimés.
- Op 20 absente de ListeSuivi du jour mais présente dans l’historique SuiviOperations.

## 7. Calculs alimentés après promotion

| Calcul | Branchement |
|--------|-------------|
| Commandes / demande restante | `sales_orders` / `sales_order_lines` importés |
| CBN | via `sales_orders` + nomenclatures validées ; éviter besoin sur qté déjà terminée |
| Pegging | liens commande → ligne → OF → ops / composants |
| Charge restante | `MontepullRemainingLoad` : qté restante × temps unitaire **validé** (ops terminées = 0) |
| Gantt APS | OF / ops réels (`actual_*`) vs planifiés (`planned_*`) |
| Backtest MVP-0 | historique durées / quantités / rejets (`mo_production_events`, `mp_duration_stats`) |
| OTIF / retard | `delivery_date` vs `actual_end` lorsque disponibles |

## 8. Interface

Page `/imports/montepull` : multi-fichiers, détection de type, preview, validation, anomalies, historique, relance, annulation logique, export CSV anomalies, choix dataset `DEMO` | `MONTEPULL_REAL`.

## 9. Fixtures de test

Copies réduites anonymisées sous `tests/fixtures/montepull/` (générées par `scripts/create_montepull_fixtures.py`) — **aucun chemin absolu machine** dans les tests.
