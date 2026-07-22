# État du projet Axioplan / Montepull

**Date du rapport :** 2026-07-10  
**Base :** contenu du dépôt `Montepull` au moment de l'audit (code, SQL, tests, documentation).  
**Méthode :** lecture du code source, schémas SQL, exécution des tests .NET et Python sur l'environnement local.

---

# 1. Objectif du projet

## But général

Développer **Axioplan**, une application destinée à remplacer progressivement certaines limitations de **Sage** pour Montepull (textile). Le MVP porte sur :

- **Gammes & nomenclatures** (génération taille/couleur, BOM, gammes)
- **Configurateur articles**
- **CBN** (calcul des besoins nets)
- **Pegging** (liens besoin ↔ offre)
- **Imports Excel** (commandes, nomenclatures)
- **Simulation MRP** isolée (module `sim_*`)

Source : `docs/contexte_du_projet.md`, `Roadmap.md`, structure du code.

## Contexte Montepull

- Entreprise textile.
- Projet réalisé dans le cadre d'un stage / MVP.
- Données de démo issues de fichiers prod (ex. pull col rond, DOUBLYGILF dérivé de `docs/prod (1).md`).
- Jeu pantalon (`PANTALON_BASE`) dans le module Simulation MRP.

## Ce qui est simulé

| Élément | Détail |
|---|---|
| Référentiel articles / BOM / gammes de base | Seeds SQL (`002_prod_example`, `006_multilevel_bom`, etc.) |
| Coefficients consommation / temps | Statut `SIMULATED` ou `CONFIRMED` selon seed |
| Unités de temps gammes | Souvent `TO_CONFIRM` |
| Comportements BOM `COPY_TO_VALIDATE` | Lignes « À confirmer » en CBN |
| Module Simulation MRP entier | Tables `sim_*`, flag `is_simulated` |
| Gamme pantalon (onglet Simulation) | Structure inspirée prod, **temps fictifs** (`PantalonGammeTemplate.cs`) |
| Commande DOUBLYGILF | Seed dérivé, fichiers Excel réels non fournis dans le dépôt (`004_doublygilf.sql`) |

## Ce qui est réel (dans le périmètre MVP)

| Élément | Détail |
|---|---|
| Persistance SQL Server | Base `AxioplanMvp` |
| Application Blazor Server | `.NET 8`, port `5280` |
| Moteurs métier C# | Domain (`GenerationEngine`, `CbnEngine`, `PeggingEngine`, `SimulationCbnEngine`, etc.) |
| UI interactive | Paramètres, Articles, Imports, CBN, Pegging, Simulation, Simulation MRP |
| Formule de besoin configurable | Table `requirement_formulas`, UI `/parametres` |
| Tests automatisés | 20 tests xUnit + 32 tests Python `unittest` |

## Hors périmètre (explicitement absent ou non implémenté)

- Connexion **Sage** réelle
- Intégration **Axioplan** production / ERP externe
- **IA Planning** (`src/axioplan/modules/planning_ai/` = stub Python)
- API HTTP dédiée (Blazor Server uniquement)
- Pegging complet métier (capacités, calendriers, règles d'allocation validées)
- Zone gelée CBN (BR-CBN-004 mentionnée dans docs, non implémentée)
- Authentification / multi-utilisateurs
- Ordonnancement atelier
- Module **Consultation** : page supprimée du menu et du dépôt (`Consultation.razor` absent) ; services backend `ConsultationService` / `SqlServerConsultationRepository` encore présents

---

# 2. Architecture

## Structure de la solution

```
Montepull/
├── Axioplan.GammesNomenclatures.sln
├── database/           # Schéma SQL, seeds, migrations
├── docs/               # Documentation fonctionnelle et technique
├── scripts/            # init_db.py, run_web.ps1
├── src/
│   ├── Axioplan.GammesNomenclatures.Domain/
│   ├── Axioplan.GammesNomenclatures.Application/
│   ├── Axioplan.GammesNomenclatures.Infrastructure/
│   ├── Axioplan.GammesNomenclatures.Web/
│   └── axioplan/       # Ancien code Python (miroir partiel Domain)
├── tests/              # Python unittest + xUnit
├── Roadmap.md
└── requirements.txt
```

## Projets .NET

| Projet | Rôle |
|---|---|
| `Axioplan.GammesNomenclatures.Domain` | Moteurs purs : génération, CBN, pegging, formules, simulation MRP |
| `Axioplan.GammesNomenclatures.Application` | Services applicatifs, DTOs, interfaces repository |
| `Axioplan.GammesNomenclatures.Infrastructure` | ADO.NET / SQL Server, import Excel, logging |
| `Axioplan.GammesNomenclatures.Web` | Blazor Server, pages UI |
| `Axioplan.GammesNomenclatures.Domain.Tests` | Tests unitaires Domain/Infrastructure import |

**Stack :** .NET 8, Blazor Server (`InteractiveServer`), `Microsoft.Data.SqlClient`, pas d'Entity Framework.

## Couches

```
Web (Razor) → Application (Services) → Abstractions (Interfaces) → Infrastructure (Repositories) → SQL Server
                                      ↘ Domain (moteurs sans I/O)
```

Enregistrement DI : `Application/DependencyInjection.cs`, `Infrastructure/DependencyInjection.cs`.

Repositories principaux :

- `SqlServerSimulationRepository` — ancienne simulation pull (encore utilisée par `Cbn.razor` pour familles)
- `SqlServerSimulationCbnRepository` — module Simulation MRP (`sim_*`)
- `SqlServerParameterRepository`
- `SqlServerCbnRepository`
- `SqlServerPeggingRepository`
- `SqlServerArticleRepository`
- `SqlServerImportRepository`
- `SqlServerConsultationRepository` (backend seul)

## Structure SQL Server

Deux univers coexistent :

1. **Schéma production MVP** (`database/schema.sql`) — ~50 tables métier
2. **Schéma simulation MRP** (`database/sim_cbn_schema.sql` + `EnsureSchemaAsync`) — 15 tables `sim_*` / `simulations`

Initialisation :

- `python scripts/init_db.py --reset` : recrée tout + seeds + `sim_cbn_schema.sql`
- `python scripts/init_db.py` (sans `--reset`) : `migrate.sql` + seed formule, conserve les données ; tables `sim_*` créées au démarrage app si absentes

Connexion : `appsettings.json` → `Database:ConnectionString` (Windows Auth, `localhost\SQLEXPRESS`, base `AxioplanMvp`).

---

# 3. Base de données

## Tables principales (schéma MVP — `schema.sql`)

### Référentiel articles

| Table | Rôle |
|---|---|
| `article_categories` | Catégories articles |
| `article_families` | Familles articles |
| `articles` | Articles (PF, SF, composants, services) |
| `attribute_definitions` | Définitions d'attributs (SIZE, COLOR, GAUGE, …) |
| `attribute_options` | Valeurs d'attributs |
| `attribute_formatting_rules` | Règles de formatage code/libellé |
| `article_family_attributes` | Attributs par famille |
| `article_attribute_values` | Valeurs attributs sur articles |
| `customer_article_family_configurations` | Config client × famille |
| `customer_article_family_attributes` | Matrice attributs client |
| `configuration_sessions` / `configuration_inputs` | Sessions configurateur |
| `article_duplication_sessions` / `article_duplication_changes` | Duplication article |
| `customers` | Clients |

### Gammes & nomenclatures

| Table | Rôle |
|---|---|
| `product_families` | Familles produit fini (ex. `PULL_COL_ROND`) |
| `bom_bases` / `bom_base_lines` | Nomenclatures de base |
| `bom_line_generation_rules` | Règles génération lignes BOM |
| `routing_bases` / `routing_base_operations` | Gammes de base |
| `routing_operation_generation_rules` | Règles génération opérations |
| `generation_profiles` | Profils de génération |
| `generation_profile_dimensions` | Dimensions profil (taille, couleur) |
| `consumption_coefficients` | Coefficients consommation (arguments formule) |
| `time_coefficients` | Coefficients temps |
| `requirement_formulas` | Formule de besoin par famille |
| `generation_sessions` | Sessions génération |
| `generated_variants` | Variantes générées |
| `proposed_components` | Composants proposés |
| `bom_generated_versions` / `bom_generated_lines` | BOM générées |
| `routing_generated_versions` / `routing_generated_operations` | Gammes générées |
| `flattened_bom_lines` / `flattened_routing_lines` | Nomenclature / gamme aplatie |
| `calculation_traces` / `generation_alerts` | Traces et alertes génération |
| `workcenters` | Centres de charge |
| `units_of_measure` / `unit_conversion_rules` | Unités |

### CBN & commandes

| Table | Rôle |
|---|---|
| `sales_orders` / `sales_order_lines` | Commandes client |
| `cbn_runs` | Exécutions CBN |
| `cbn_flattened_bom_lines` | BOM aplatie par run CBN |
| `cbn_material_requirements` | Besoins matière calculés |
| `cbn_calculation_traces` | Traces calcul CBN |
| `article_bom_assignments` | Lien article PF ↔ BOM |

### Pegging & supply

| Table | Rôle |
|---|---|
| `purchase_orders` / `purchase_order_lines` | Ordres d'achat |
| `manufacturing_orders` / `manufacturing_order_lines` | Ordres de fabrication |
| `stock_balances` | Stocks |
| `pegging_runs` | Exécutions pegging |
| `pegging_links` | Liens pegging |
| `pegging_link_versions` | Versions de liens |

### Paramètres MVP & imports

| Table | Rôle |
|---|---|
| `mvp_parameter_groups` / `mvp_parameters` / `mvp_parameter_values` | Paramètres globaux CBN/Pegging |
| `import_sessions` / `import_session_warnings` | Sessions import |
| `import_mapping_profiles` | Profils mapping Excel persistés |
| `application_logs` | Journal applicatif |

## Tables Simulation MRP (`sim_cbn_schema.sql`)

| Table | Rôle |
|---|---|
| `simulations` | En-tête simulation |
| `sim_articles` | Articles (template, composants, variantes générées) |
| `sim_sales_orders` | Commandes simulées |
| `sim_bom_lines` | Lignes nomenclature |
| `sim_routing_operations` | Opérations gamme |
| `sim_variant_definitions` | Variantes générées (métadonnées) |
| `sim_duplication_options` | Tailles/couleurs persistées (coefficient, sélection) |
| `sim_component_substitution_rules` | Substitution couleur (ex. TISSU_BASE → TISSU_NOIR) |
| `sim_cbn_parameters` | Stock, sécurité, encours, règles de lot |
| `sim_gross_requirements` | Besoins bruts |
| `sim_net_requirements` | Besoins nets |
| `sim_planned_orders` | Ordres planifiés OF/OA |
| `sim_work_orders` | Ordres de fabrication simulés |
| `sim_pegging` | Arbre de pegging |
| `sim_cbn_alerts` | Alertes CBN simulation |

## Vues SQL

| Vue | Rôle |
|---|---|
| `usable_generation_profiles` | Profils `generation_profiles` avec `status = 'VALIDATED'` |

## Procédures stockées

Aucune procédure stockée dans les scripts du dépôt. Accès données via SQL inline dans les repositories C#.

## Relations importantes

- `product_families` → `article_families` (via `article_family_id`)
- `bom_base_lines` → `articles` (composants)
- `sales_order_lines` → `articles` + options taille/couleur
- `cbn_runs` → `sales_orders` + `product_families`
- `pegging_runs` → `cbn_runs`
- `sim_*` : toutes filtrées par `simulation_id`

---

# 4. Modules existants

## Gammes & Nomenclatures

| | |
|---|---|
| **Rôle** | Génération variantes taille×couleur, calcul quantités BOM et temps gamme |
| **Code** | `GenerationEngine.cs`, `SqlServerSimulationRepository.cs` (mode historique pull), seeds `002`, `006` |
| **Fonctionnalités** | Combinaisons taille/couleur ; coefficients ; formule configurable ; traces |
| **État** | **Partiellement déplacé** : l'onglet `/` utilise désormais `GammesSimulationService` + données `sim_*` (pantalon). L'ancien flux `SqlServerSimulationRepository.SimulateAsync` (famille `PULL_COL_ROND`) existe encore en code mais n'est plus exposé dans le menu principal |

## Configurateur Articles

| | |
|---|---|
| **Rôle** | CRUD articles, configuration, duplication, gouvernance attributs |
| **Code** | `ArticleService`, `SqlServerArticleRepository`, `Articles.razor` |
| **Fonctionnalités** | Création manuelle, configurée, duplication ; catégories/familles ; attributs ; matrice client ; traçabilité ; lien BOM/gammes |
| **État** | **Fonctionnel** (UI complète, persistance SQL) |

## Paramètres

| | |
|---|---|
| **Rôle** | Coefficients, pertes BOM, formule de besoin, arguments calcul |
| **Code** | `ParameterService`, `SqlServerParameterRepository`, `Parametres.razor` |
| **Fonctionnalités** | Configurateur formule visuel ; arguments/valeurs ; coefficients ; pertes ; paramètres MVP ; aperçu calcul |
| **État** | **Fonctionnel** |

## Simulation (onglet `/`)

| | |
|---|---|
| **Rôle** | Aperçu gammes & nomenclatures à partir des données Simulation MRP |
| **Code** | `GammesSimulationService`, `PantalonGammeTemplate`, `Simulation.razor` |
| **Fonctionnalités** | Sélection article `sim_*` ; tailles/couleurs synchronisées ; résumé BOM taille M ; résumé gamme (30 opérations) ; stats combinaisons |
| **État** | **Fonctionnel** (données pantalon ; pas de génération persistante de variantes sur cet onglet) |

## Simulation MRP (`/simulation-mrp`)

| | |
|---|---|
| **Rôle** | Module CBN/MRP isolé |
| **Code** | `SimulationCbnEngine`, `SimulationDuplicationEngine`, `SqlServerSimulationCbnRepository`, `SimulationMrp.razor` |
| **Fonctionnalités** | CRUD simulations ; seed PANTALON ; duplication taille/couleur ; CBN complet (LLC, explosion, brut/net, OF/OA, pegging, alertes, justifications ordres) ; persistance tailles/couleurs |
| **État** | **Fonctionnel** (module le plus complet côté MRP) |

## CBN production (`/cbn`)

| | |
|---|---|
| **Rôle** | Calcul besoins nets depuis commandes réelles seed/import |
| **Code** | `CbnEngine`, `BomFlattener`, `SqlServerCbnRepository`, `Cbn.razor` |
| **Fonctionnalités** | Multi-niveaux PF→SF→composants ; formule configurable ; matières variables / fournitures fixes / à confirmer ; persistance run |
| **État** | **Fonctionnel** ; page **absente du menu** (`MainLayout.razor`) mais accessible par URL |

## Pegging (`/pegging`)

| | |
|---|---|
| **Rôle** | Lier besoins CBN à OV / OF / OA / stock |
| **Code** | `PeggingEngine`, `SqlServerPeggingRepository`, `Pegging.razor` |
| **Fonctionnalités** | Lancement sur run CBN ; liens ; couverture besoins ; disponibilités ; arbre cascade |
| **État** | **Fonctionnel (V1)** ; page **absente du menu** ; règles d'allocation documentées comme « À confirmer » |

## Imports (`/imports`)

| | |
|---|---|
| **Rôle** | Import Excel commandes et nomenclatures |
| **Code** | `ImportService`, `BomImportEngine`, `SqlServerImportRepository`, `Imports.razor` |
| **Fonctionnalités** | Analyse workbook ; mapping colonnes ; prévisualisation ; profils mapping ; import ORDER et BOM |
| **État** | **Fonctionnel** |

## Consultation

| | |
|---|---|
| **Rôle** | Consultation gammes, BOM, runs CBN |
| **État** | **Supprimée de l'UI** ; backend `ConsultationService` encore utilisé par `Pegging.razor` (liste runs CBN) |

---

# 5. Pages Blazor

| URL | Fichier | Menu | Rôle | Fonctionnalités |
|---|---|---|---|---|
| `/` | `Simulation.razor` | Oui | Simulation gammes & nomenclatures | Articles `sim_*` ; tailles/couleurs persistées ; résumé BOM M ; résumé gamme pantalon ; bouton Simuler |
| `/simulation-mrp` | `SimulationMrp.razor` | Oui | Simulation MRP | Créer simulation ; seed PANTALON ; duplication ; générer variantes ; BOM/gammes ; params CBN ; lancer CBN ; résultats (BOM aplatie, bruts, nets, ordres avec justification, OF, pegging, alertes) |
| `/imports` | `Imports.razor` | Oui | Imports Excel | Import commande ; import nomenclature ; mapping ; prévisualisation |
| `/parametres` | `Parametres.razor` | Oui | Paramètres & formule | Arguments ; coefficients ; pertes ; formule ; aperçu |
| `/articles` | `Articles.razor` | Oui | Configurateur articles | 9 onglets (liste, manuel, configuré, duplication, catégories, attributs, matrice, traçabilité, BOM/gammes) |
| `/cbn` | `Cbn.razor` | **Non** | CBN production | Sélection commande/famille ; calcul ; affichage besoins |
| `/pegging` | `Pegging.razor` | **Non** | Pegging | Sélection run CBN ; calcul liens ; couverture |

**Layout :** `MainLayout.razor` — navigation 5 liens (Simulation, Simulation MRP, Imports, Paramètres, Articles).

**Mode rendu :** `InteractiveServer` sur les pages métier.

---

# 6. Paramètres

## Gestion des arguments

- Les « arguments de calcul » sont des entrées `attribute_definitions` avec `is_formula_argument = 1` et `is_active = 1`.
- Seed initial : `SIZE`, `COLOR`, `GAUGE` (`008_formula_configurator.sql`).
- UI `/parametres` : liste arguments, création, modification libellé, suppression argument.

## Création argument

- `ParameterService.CreateCalculationArgumentAsync` → insert `attribute_definitions` + rattachement famille via coefficients.
- Schéma assuré par `EnsureFormulaSchemaAsync` au besoin.

## Ajout valeurs

- `CreateArgumentValueAsync` : crée `attribute_options` + coefficients consommation par défaut (`1.0`) pour toutes les familles (`EnsureConsumptionCoefficientsForFamilyAsync`).
- UI : bouton ajout valeur, statut option (VALIDATED, etc.), suppression valeur.

## Modification coefficients

- Tables `consumption_coefficients` et `time_coefficients`.
- UI : grille éditable par famille ; mise à jour via `UpdateConsumptionCoefficientAsync` / `UpdateTimeCoefficientAsync`.
- Marquage recalcul CBN si paramètre MVP `CBN_AUTO_RECALC_ENABLED` actif (`MarkCbnRecalcIfEnabledAsync`).

## Formule métier

- Table `requirement_formulas` : `expression`, `display_expression`, `apply_order_quantity`, `target = 'REQUIREMENT'`.
- Défaut seed : `BesoinBase * SIZE * COLOR * GAUGE`.
- UI configurateur : construction par tokens (arguments + opérateurs `+ - * /` et parenthèses).

## Stockage SQL

- `requirement_formulas` (par `product_family_id`)
- `consumption_coefficients` (coefficient par famille × attribut × option)
- `attribute_definitions` / `attribute_options`
- `bom_base_lines.loss_rate` (pertes par ligne)

## Utilisation dans Simulation

- L'onglet `/` actuel **n'utilise pas** `requirement_formulas` du schéma MVP : il lit BOM `sim_*` et coefficients taille M codés dans `sim_bom_lines` / `sim_duplication_options`.
- L'ancien `SqlServerSimulationRepository.SimulateAsync` utilisait formule + coefficients (`consumption_coefficients`, `time_coefficients`) pour la famille pull — code toujours présent.

## Utilisation dans CBN

- `SqlServerCbnRepository.RunCbnAsync` charge `requirement_formulas` pour la famille.
- `CbnEngine.ComputeRequirement` : lignes `CALCULATED` → `FormulaEngine` / `GenerationEngine.CalculateNetQuantityFromFormula` ; autres comportements → FIXED ou TO_CONFIRM.
- Coefficients : map construite via `BuildArgumentCoefficientMapAsync` (SIZE, COLOR, GAUGE, …).

---

# 7. Simulation

## Deux flux distincts dans le code

### A. Onglet `/` — `GammesSimulationService`

1. `EnsureContextAsync` : première simulation (ou création), seed pantalon, options duplication par défaut.
2. Charge articles `sim_articles`, options `sim_duplication_options`.
3. Au clic **Simuler** :
   - Compte combinaisons taille×couleur sélectionnées
   - BOM base taille **M** (coefficient 1.0, sans substitution couleur) depuis `sim_bom_lines`
   - Gamme : template statique `PantalonGammeTemplate` (30 opérations, temps fictifs, quantités composants depuis BOM M)
4. **Ne génère pas** de variantes en base sur cet onglet.
5. Sauvegarde sélections tailles/couleurs dans `sim_duplication_options`.

### B. Onglet `/simulation-mrp` — duplication + CBN

1. **Duplication** (`SimulationDuplicationEngine` + `GenerateVariantsAsync`) :
   - Template article (ex. `PANTALON_BASE`)
   - Pour chaque taille×couleur cochée : crée article variante, BOM (coef taille, substitution couleur), gamme (coef temps), params CBN
2. **CBN** (`SimulationCbnEngine.Run`) :
   - Détection cycles BOM
   - LLC (Low-Level Code)
   - Explosion contrôlée → `FlatBomLine`
   - Besoin brut (commande + dépendant)
   - Besoin net : `brut + réservé + sécu − stock − encours OF − achats` (plancher 0)
   - Lot sizing (`LotForLot`, min, multiple)
   - OF si `Manufactured`, OA si `Purchased`
   - Pegging arbre
   - Justification textuelle par ordre planifié

## Données utilisées (Simulation MRP)

- Seed démo : `PANTALON_BASE` + composants `TISSU_BASE`, `TISSU_NOIR`, `TISSU_BLEU`, `FIL`, `BOUTON`, `ZIP`, `EMBALLAGE`
- BOM template : qty/par 1.20 (tissu, coef taille), 0.05 (fil), 1 (bouton, zip, emballage)
- Substitutions couleur : Noir → `TISSU_NOIR`, Bleu → `TISSU_BLEU`

## Création variantes

- Code variante : `PANTALON_{COULEUR}_{TAILLE}` (`SimulationDuplicationEngine.BuildVariantCode`)
- Persistance : `sim_articles`, `sim_bom_lines`, `sim_routing_operations`, `sim_cbn_parameters`, `sim_variant_definitions`

## Calcul des besoins (Simulation MRP)

Voir section 8 (moteur `SimulationCbnEngine`) — isolé du CBN production.

---

# 8. CBN

## Deux moteurs

| Moteur | Usage | Fichier |
|---|---|---|
| `CbnEngine` | CBN production (`/cbn`) | `Domain/CbnEngine.cs` |
| `SimulationCbnEngine` | Simulation MRP | `Domain/Simulation/SimulationCbnEngine.cs` |

## CBN production — calcul

1. Sélection commande + famille produit
2. Validation préconditions (`ValidateCbnPreconditionsAsync`)
3. Chargement lignes commande pour la famille
4. Aplatissement BOM multi-niveaux (`BomFlattener`, profondeur `PEGGING_MAX_CASCADE_LEVEL`, défaut 5)
5. Pour chaque ligne commande × ligne BOM aplatie :
   - Coefficients arguments (SIZE, COLOR, GAUGE, …)
   - `CbnEngine.ComputeRequirement`
6. Agrégation totaux par composant
7. Persistance : `cbn_runs`, `cbn_flattened_bom_lines`, `cbn_material_requirements`, `cbn_calculation_traces`

## Matières variables

- Lignes BOM `behavior = 'CALCULATED'`
- Mode `VARIABLE_MATERIAL`
- Formule `requirement_formulas` ou repli `BesoinBase * SIZE * COLOR`

## Fournitures fixes

- Lignes `FIXED` (ou non CALCULATED)
- Mode `FIXED_SUPPLY`
- Calcul : `quantity_base * order_quantity` (+ perte si `loss_rate > 0`)

## Lignes « À confirmer »

- `behavior = 'COPY_TO_VALIDATE'` → mode `TO_CONFIRM` dans `CbnEngine`
- Présentes dans seed prod exemple (vignette, sachet, etc.)

## Persistance SQL

Tables `cbn_*` listées section 3.

## Affichage (`Cbn.razor`)

- Lignes commande
- Nomenclature aplatie
- Besoins par variante
- Totaux par composant
- Traces de calcul

---

# 9. Imports

## Import commandes

- Cible : `import_target = 'ORDER'`
- Analyse Excel : feuilles, lignes métadonnées, tailles, quantités
- Mapping modifiable (métadonnées, colonnes taille)
- Création `sales_orders` / `sales_order_lines`
- Création auto options taille/couleur manquantes (coefficient 1.0)
- Exemple documenté : `DOUBLYGILF.xlsx`

## Import nomenclatures

- Cible : `import_target = 'BOM'`
- Moteur : `BomImportEngine` (détection en-tête, colonnes, lignes ambiguës)
- Prévisualisation : `PreviewBomImportAsync`
- Crée nouvelle version `bom_bases` + lignes pour famille cible
- Statut lignes ambiguës : confirmation requise

## Mapping

- Profils persistés : `import_mapping_profiles` (fingerprint structure + JSON mappings)
- Rechargement profils par type d'import

## Prévisualisation

- UI tableaux avant validation import commande et BOM
- Warnings dans `import_session_warnings`

## Limitations (constatées dans le code / docs)

- Warnings n'empêchent pas import si données minimales présentes
- Fichiers DOUBLYGILF réels non versionnés dans le dépôt
- Pas de validation métier post-import automatisée au-delà des warnings
- Import ne déclenche pas CBN automatiquement

---

# 10. Formules

## Moteur (`FormulaEngine.cs`)

- Tokenisation expression (`+`, `-`, `*`, `/`, parenthèses, identifiants)
- Token spécial `BesoinBase` = quantité nomenclature de base
- Autres tokens = codes arguments (SIZE, COLOR, GAUGE, …) → coefficients numériques
- Évaluation : algorithme shunting-yard / piles opérandes-opérateurs
- Validation : rejette tokens inconnus ; test évaluation avec coefficients 1.0

## Construction expressions

- UI `/parametres` : ajout tokens via clic
- Sauvegarde : `display_expression` + `expression` normalisée

## Évaluation

- Simulation/CBN : `GenerationEngine.CalculateNetQuantityFromFormula` → `FormulaEngine.Evaluate`
- Brut : `GenerationEngine.CalculateGrossQuantity(net, lossRate)` → `net / (1 - loss)` si perte > 0

## Enregistrement

- Table `requirement_formulas`
- Un enregistrement par `(product_family_id, target)` ; `target = 'REQUIREMENT'` utilisé actuellement

---

# 11. Tests

## Tests .NET (`tests/Axioplan.GammesNomenclatures.Domain.Tests`)

**Résultat exécution 2026-07-10 : 20/20 passés**

| Test | Sujet |
|---|---|
| `BomImportDiagnosticTests.DiagnoseRealBomFile_ListsExtractedFournitures` | Diagnostic import BOM réel |
| `BomImportEngineTests.*` (4) | Détection en-tête, mapping, lignes ambiguës, colonnes fusionnées |
| `BomFlattenerTests.Flatten_multi_level_pf_sf_component` | Aplatissement multi-niveaux |
| `PeggingEngineTests.*` (2) | Liens OV/OA, priorité stock |
| `CbnEngineTests.*` (3) | Variable, fixe, TO_CONFIRM |
| `ExcelImportAnalysisTests.*` (2) | Analyse workbooks commande/BOM |
| `FormulaEngineTests.*` (3) | Évaluation, validation, repli sans expression |
| `SimulationCbnEngineTests.*` (4) | Duplication pantalon, CBN OF/OA, cycles, plancher net |

## Tests Python (`tests/`, `unittest`)

**Résultat exécution 2026-07-10 : 32/32 passés**

| Fichier | Nb tests (approx.) | Sujet |
|---|---|---|
| `test_database_mvp.py` | 6 | Schéma, seeds, profils validés |
| `test_generation.py` | 4 | Moteur génération Python |
| `test_cbn.py` | 3 | Tables CBN, run persistant |
| `test_multilevel_cbn.py` | 2 | BOM multi-niveaux, FIL-MINT |
| `test_pegging.py` | 3 | Tables pegging, seed |
| `test_parameters.py` | 4 | Paramètres MVP, coefficients |
| `test_article_configurator.py` | 6 | Configurateur article |
| `test_doublygilf.py` | 4 | Jeu DOUBLYGILF |

**Note :** `pytest` non installé dans l'environnement audité ; `python -m unittest discover` fonctionne.

---

# 12. Documentation

| Document | Contenu | Validité |
|---|---|---|
| `docs/contexte_du_projet.md` | Contexte Montepull, objectifs | **Partiellement valide** (vision globale OK ; détails UI obsolètes) |
| `docs/RAPPORT-FINAL-MVP.md` | Rapport MVP 2026-07-06 | **Obsolète** (menu, Consultation, nb tests, onglet Simulation) |
| `docs/guide-utilisateur.md` | Guide pages et scénarios | **Obsolète** (Consultation, CBN/Pegging menu, Simulation = pull tags) |
| `docs/a-confirmer.md` | Points métier ouverts | **Valide** (référence écarts connus) |
| `docs/doublygilf-validation.md` | Validation jeu DOUBLYGILF | **Valide** (écarts documentés) |
| `docs/prod (1).md` | Exemple gamme prod pull | **Valide** (référence source) |
| `docs/dotnet-audit-architecture-actuelle.md` | Audit architecture | **Partiellement obsolète** (évolution Simulation MRP) |
| `docs/dotnet-architecture-proposee.md` | Architecture cible | **Référence** (pas état actuel) |
| `docs/architecture-mvp.md` | Architecture MVP | **Partiellement obsolète** |
| `docs/modele-donnees.md` | Modèle données | **Partiellement obsolète** (tables `sim_*` absentes) |
| `docs/domain-model-gammes-nomenclatures.md` | Modèle domaine | **Référence conceptuelle** |
| `docs/business-rules-gammes-nomenclatures.md` | Règles métier | **Référence** |
| `docs/flux-generation.md` | Flux génération | **Référence** (flux pull ; pas sim MRP) |
| `docs/use-cases-gammes-nomenclatures.md` | Cas d'usage | **Référence** |
| `docs/decoupage-modules.md` | Découpage modules | **Référence** |
| `docs/sprints-developpement-dotnet.md` | Sprints | **Historique** |
| `docs/Cadrage_fonctionnel_configurateur_article (1).md` | Cadrage articles | **Référence cadrage** |
| `docs/Configurateur_Gammes_Nomenclatures (1).md` | Cadrage gammes | **Référence cadrage** |
| `docs/sources/cahierdecadrageetdescharges_nomenclatures.md` | Cahier des charges | **Référence** (périmètre cible long terme) |
| `Roadmap.md` | Feuille de route | **Historique / planification** |

**Document créé par cet audit :** `docs/ETAT_PROJET.md` (ce fichier).

---

# 13. Ce qui reste à faire

## Critique

- [ ] Réconcilier **deux univers Simulation** : onglet `/` (pantalon `sim_*`) vs ancien flux pull (`SqlServerSimulationRepository`) et CBN production (`PULL_COL_ROND`)
- [ ] **Menu navigation** : pages `/cbn` et `/pegging` implémentées mais non liées ; risque de confusion pour reprise projet
- [ ] **Synchronisation simulation** : onglet `/` utilise la simulation la plus récente ; MRP permet d'en sélectionner une autre → désynchronisation possible
- [ ] Valider avec le métier les **règles pegging** (priorité stock/OF/OA) — actuellement « À confirmer » dans le code
- [ ] Valider **BOM / besoins DOUBLYGILF** vs prod réel (écarts documentés non résolus)

## Important

- [ ] Réintégrer ou remplacer **Consultation** (supprimée UI ; besoins consultation runs/BOM toujours présents côté service)
- [ ] Couverture tests **Simulation MRP** côté Infrastructure (repositories `sim_*`) — tests actuels = Domain uniquement
- [ ] **Génération réelle** variantes depuis onglet `/` (actuellement aperçu seulement)
- [ ] Brancher formule `/parametres` sur Simulation pantalon si souhaité (actuellement BOM `sim_*` sans formule configurable)
- [ ] Import fichiers DOUBLYGILF réels en environnement équipe (non présents dans le dépôt)
- [ ] Documentation utilisateur à réaligner sur l'état actuel

## Amélioration

- [ ] État actif menu (`NavClass` retourne toujours `""`)
- [ ] Unifier nommage français/anglais dans UI et code
- [ ] Réduire duplication logique Python (`src/axioplan`) / C# si Python n'est plus utilisé hors tests
- [ ] Persistance `justification` ordres simulation en SQL (calculée mais non stockée dans `sim_planned_orders`)
- [ ] API REST si intégration externe requise (hors scope MVP actuel)

---

# 14. Dette technique

| Point | Où |
|---|---|
| Deux stacks simulation (pull SQL vs `sim_*`) | `SqlServerSimulationRepository` vs `GammesSimulationService` |
| `ConsultationService` orphelin (UI supprimée) | `Pegging.razor`, `SqlServerConsultationRepository` |
| Pages CBN/Pegging accessibles mais hors menu | `MainLayout.razor` |
| `SqlServerSimulationRepository` encore injecté dans `Cbn.razor` pour familles | Couplage |
| Schéma `sim_*` créé à la volée (`EnsureSchemaAsync`) + script séparé | Risque divergence environnements |
| `init_db` sans `--reset` n'applique pas `sim_cbn_schema.sql` | Dépend du démarrage app |
| Gamme pantalon : template **codé en dur** (`PantalonGammeTemplate`) | Pas en base |
| Temps gammes `TO_CONFIRM` / comportements `COPY_TO_VALIDATE` | Seeds prod exemple |
| Règles pegging simplifiées (`min(besoin, offre)`) | `PeggingEngine.cs` |
| Pas d'EF / pas de migrations versionnées type Flyway | Scripts manuels |
| Code Python legacy partiel | `src/axioplan/` |
| Tests import diagnostic dépendent fichiers externes potentiels | `BomImportDiagnosticTests` |
| `application_logs` : logger présent, usage limité | `SqlApplicationLogger` |
| BR-CBN-004 zone gelée non implémentée | Docs |
| Collection expressions C# 12 `[]` vs .NET 8 — résolu par `Array.Empty` dans certains fichiers | Historique build |

---

# 15. Fonctionnalités terminées

- [x] Schéma SQL Server MVP complet (`schema.sql`)
- [x] Seeds référentiels, prod exemple, articles, DOUBLYGILF, paramètres, multi-niveaux, pegging, formule
- [x] Application Blazor Server .NET 8
- [x] Configurateur articles (UI multi-onglets)
- [x] Paramètres : coefficients, pertes, formule configurable, arguments/valeurs CRUD
- [x] Import Excel commandes et nomenclatures avec mapping
- [x] CBN production multi-niveaux avec traces
- [x] Pegging V1 avec persistance
- [x] Module Simulation MRP complet (`sim_*`)
- [x] Duplication taille/couleur pantalon
- [x] CBN simulation (LLC, brut, net, OF/OA, pegging, alertes, justifications)
- [x] Persistance tailles/couleurs duplication (`sim_duplication_options`)
- [x] Onglet Simulation synchronisé avec données MRP (BOM M, gamme template)
- [x] Moteur formules (`FormulaEngine`)
- [x] Tests : 20 xUnit + 32 Python unittest verts
- [x] Scripts `init_db.py` et `run_web.ps1`
- [x] Journalisation SQL (`application_logs`)

---

# 16. Fonctionnalités non terminées

- [ ] Intégration Sage / ERP réel
- [ ] Pegging métier complet (règles validées, capacité, calendriers)
- [ ] Consultation UI
- [ ] Génération persistante variantes depuis onglet `/`
- [ ] Unification simulation pull et simulation MRP
- [ ] Zone gelée CBN (BR-CBN-004)
- [ ] API HTTP dédiée
- [ ] Authentification / autorisation
- [ ] IA Planning
- [ ] Validation métier complète DOUBLYGILF / prod
- [ ] Ordonnancement atelier
- [ ] Documentation utilisateur à jour
- [ ] Couverture tests Integration/Web

---

# 17. Résumé final

**Axioplan Montepull** est un MVP **Blazor Server + SQL Server** couvrant configurateur articles, paramètres de formule, imports Excel, CBN multi-niveaux, pegging V1, et un **module Simulation MRP isolé** (pantalon) avec duplication et MRP classique. L'architecture en couches Domain / Application / Infrastructure / Web est en place ; la persistance est **ADO.NET** sans ORM.

**État fonctionnel :** l'application démarre sur `http://localhost:5280` après `python scripts/init_db.py` et `.\scripts\run_web.ps1`. Les tests automatisés passent (**20 .NET**, **32 Python**). Le menu expose 5 pages ; **CBN** et **Pegging** restent accessibles par URL directe. **Consultation** a été retirée de l'UI.

**Données :** deux jeux coexistent — famille **pull col rond** (`PULL_COL_ROND`, DOUBLYGILF) dans le schéma MVP, et jeu **pantalon** dans `sim_*`. L'onglet **Simulation** (`/`) affiche désormais le pantalon MRP ; l'ancien flux pull reste dans le code pour CBN production.

**Points d'attention pour reprise :** clarifier quel flux Simulation/CBN est la cible produit ; remettre CBN/Pegging/Consultation au menu si nécessaire ; valider écarts métier DOUBLYGILF ; mettre à jour `guide-utilisateur.md` et `RAPPORT-FINAL-MVP.md`.

**Commandes essentielles :**

```powershell
python scripts/init_db.py          # migration (conserve données)
python scripts/init_db.py --reset  # réinitialisation complète
.\scripts\run_web.ps1                # http://localhost:5280
dotnet test tests\Axioplan.GammesNomenclatures.Domain.Tests
python -m unittest discover -s tests
```

---

*Rapport généré par audit du dépôt — aucune modification du code applicatif.*
