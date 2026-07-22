# Rapport final projet — Axioplan Montepull

**Date :** 2026-07-17  
**Objectif de phase :** stabilisation et validation (pas de nouveaux modules APS).  
**Livrable principal :** MVP-0 Gate 0→1, démontrable devant Montepull.

---

## 1. Bugs corrigés

### BUG-1 — `/aps/cycle-nocturne` HTTP 500 (DataReader)
- **Symptôme :** `There is already an open DataReader associated with this Connection which must be closed first.`
- **Cause :** dans `ListNightlyRunsAsync`, un `SqlDataReader` sur `aps_nightly_runs` restait ouvert pendant l’ouverture d’un second reader pour `aps_nightly_run_steps`.
- **Correction :** lecture complète des runs puis **fermeture du reader**, ensuite chargement des steps run par run (un reader à la fois).
- **Vérification :** HTTP **200** sur `/aps/cycle-nocturne` ; suite Playwright **55/55**.

### Navigation — `/pegging` orphelin
- Lien **Pegging** ajouté dans la barre principale (après CBN legacy).
- Lien mis à jour dans le texte d’aide de la page CBN.

---

## 2. Fichiers modifiés

| Fichier | Changement |
|---|---|
| `Infrastructure/Repositories/SqlServerApsPhase9Repository.cs` | Fix DataReader `ListNightlyRunsAsync` |
| `Web/Components/Layout/MainLayout.razor` | Lien Pegging |
| `Web/Components/Pages/Cbn.razor` | Lien menu Pegging |
| `Infrastructure/Imports/BomImportEngine.cs` | Alias mapping élargis + `ListUnmappedHeaders` |
| `Infrastructure/Repositories/SqlServerImportRepository.cs` | Warnings colonnes inconnues + `LoadWorkbook` robuste |
| `Infrastructure/Mvp0/Mvp0ExcelCsvConverter.cs` | **Nouveau** — Excel → CSV pour MVP-0 |
| `Infrastructure/Mvp0/SqlServerMvp0Repository.cs` | `ConvertExcelToCsvSheetsAsync` |
| `Application/Abstractions/IMvp0Repository.cs` | Contrat conversion Excel |
| `Application/Mvp0/Mvp0WorkflowService.cs` | `ConvertExcelAsync` |
| `Web/Components/Pages/Mvp0/Mvp0Imports.razor` | Accept `.xlsx`/`.xls` + choix de feuille |
| `Web/Components/Pages/Imports.razor` | Texte lead clarifié |
| `tests/.../BomImportEngineTests.cs` | Test colonnes non mappées |

---

## 3. Tests exécutés

```text
dotnet build Axioplan.GammesNomenclatures.sln
dotnet test tests/Axioplan.GammesNomenclatures.Domain.Tests
dotnet test tests/Axioplan.GammesNomenclatures.E2E.Tests
```

HTTP manuel : `/aps/cycle-nocturne` → 200, `/pegging` → 200.

---

## 4. Résultats des tests

| Suite | Résultat |
|---|---|
| Build solution | **OK** (0 erreur ; 1 warning préexistant CTP nullable) |
| Domain unit tests | **117 / 117 OK** (+1 test colonnes non mappées) |
| Playwright E2E | **55 / 55 OK** (était 54/55 avant le fix cycle nocturne) |

Aucun échec Playwright à capturer.

---

## 5. Fonctionnalités validées (démo Montepull)

### MVP-0 (livrable principal)
Parcours validé E2E :

Campagne → Import (seed / CSV / Excel) → Validation → Anomalies → By-pass → Fiabilité → Backtest → Avis planificateur → Gate → Rapport

- SIMULATED → Gate `INSUFFICIENT_DATA` (pas de GO final)
- REAL propre → Gate `GO` possible
- Backtest bloqué si BLOCKING non by-passés

### Legacy
- Articles, Paramètres, Simulation, Simulation MRP, Imports Excel, CBN, **Pegging (menu)**

### Import réel
- Imports Excel legacy : erreurs claires, colonnes inconnues **signalées**, mapping élargi
- MVP-0 : CSV + Excel (.xlsx/.xls) avec sélection de feuille, rejets non silencieux, lots versionnés

---

## 6. Fonctionnalités encore expérimentales

Tout le menu **Prototype / Expérimental** (`/aps/*`) :

Journal, Attendus, Référentiel, Compilateur, Capacités, CBN segments, Flux, Charges, CTP, Promesses, Contrats, Plans, Cycle nocturne, Recette, Nervosité.

- Utilisables en démo technique
- **Ne décident pas** du GO Gate 0→1
- Contenu souvent seed / DEMO

---

## 7. Limitations restantes

- Seuils / poids MVP-0 encore marqués `TO_CONFIRM` en SQL
- Import Excel MVP-0 = conversion feuille → CSV (pas d’éditeur de mapping cellule à cellule aussi riche que `/imports`)
- PDF Gate = impression navigateur (pas lib PDF serveur)
- APS = labo ; MVP-1 (ordo goulots) non démarré volontairement
- Warning nullable CTP préexistant non traité (hors scope stabilité)

---

## 8. Recommandations avant livraison / démo

1. **Démo officielle :** parcours MVP-0 sur **une famille REAL** Montepull (CSV/Excel réels), avis planificateur, Gate, impression rapport.
2. Montrer clairement la bannière Prototype APS : non utilisé pour le GO.
3. Préparer 1 fichier Excel commande + 1 nomenclature connus (DOUBLYGILF ou export réel).
4. Redémarrer l’app après `python scripts/init_db.py --reset`.
5. Après GO métier : seulement alors ouvrir le cadrage **MVP-1**.

---

## 9. Synthèse une phrase

L’application est **stable pour une démo Montepull** : MVP-0 bout en bout OK, legacy OK, Pegging accessible, bug cycle nocturne corrigé, imports Excel renforcés, **117 tests unitaires + 55 E2E verts**.
