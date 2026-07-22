# Audit fonctionnel complet — Axioplan Montepull

**Date :** 2026-07-17  
**Méthode :** tests réels (build, unitaires, Playwright E2E, requêtes SQL), pas lecture seule du code.  
**Environnement :** machine locale Windows, .NET 8 Web + tests E2E net10.0.

---

## 1. Environnement de test

| Élément | Valeur |
|---|---|
| OS | Windows 10/11 |
| Application | Blazor Server `Axioplan.GammesNomenclatures.Web` |
| URL | `http://localhost:5280` |
| SQL Server | `localhost\SQLEXPRESS` |
| Base | `AxioplanMvp` (réinitialisée, **pas** production) |
| Chaîne | `Server=localhost\SQLEXPRESS;Database=AxioplanMvp;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;` |
| Navigateur E2E | Playwright Chromium headless (installé pour l’audit) |

### Correction technique mineure (audit uniquement)

1. **Playwright** : navigateurs absents → installation via `playwright.ps1 install chromium` (prérequis E2E, pas de changement métier).
2. **Tests E2E** : 3 assertions Playwright corrigées (strict mode / sélecteurs ambigus) dans :
   - `tests/Axioplan.GammesNomenclatures.E2E.Tests/Mvp0WorkflowTests.cs`
   - `tests/Axioplan.GammesNomenclatures.E2E.Tests/LegacyPagesTests.cs`
3. **Redémarrage app** nécessaire après `init_db.py --reset` si l’app tournait déjà (connexions / état → HTTP 500 transitoire).

Aucun bug métier corrigé pendant l’audit.

---

## 2. Commandes exécutées

```powershell
dotnet build Axioplan.GammesNomenclatures.sln
dotnet test tests/Axioplan.GammesNomenclatures.Domain.Tests/...
python scripts/init_db.py --reset
dotnet run --project src/Axioplan.GammesNomenclatures.Web/... --urls http://localhost:5280
powershell -ExecutionPolicy Bypass -File tests/.../playwright.ps1 install chromium
dotnet test tests/Axioplan.GammesNomenclatures.E2E.Tests/...
sqlcmd -S localhost\SQLEXPRESS -E -C -d AxioplanMvp -Q "SELECT ..."
```

Journal E2E brut : `docs/audit_e2e_output.txt`

---

## 3. Résultat build / tests

| Suite | Résultat |
|---|---|
| **Build solution** | OK (0 erreur après arrêt de l’instance Web qui verrouillait les DLL) |
| **Tests Domain** | **116 / 116 OK** |
| **Tests E2E Playwright** | **54 / 55 OK** (1 échec = route `/aps/cycle-nocturne` HTTP 500 — bug réel, voir §8) |

---

## 4. Tableau de toutes les routes

| Route | HTTP (E2E) | Statut fonctionnel | Notes |
|---|---|---|---|
| `/mvp0` | 200 | OK | Campagnes + demo bout-en-bout |
| `/mvp0/campagnes` | 200 | OK | Alias de `/mvp0` |
| `/mvp0/imports` | 200 | OK | Assistant CSV |
| `/mvp0/validation` | 200 | PARTIEL | Hub → sélection campagne → workflow |
| `/mvp0/anomalies` | 200 | PARTIEL | Idem |
| `/mvp0/bypass` | 200 | PARTIEL | Idem |
| `/mvp0/fiabilite` | 200 | PARTIEL | Idem |
| `/mvp0/backtest` | 200 | PARTIEL | Idem |
| `/mvp0/gate` | 200 | PARTIEL | Idem |
| `/mvp0/rapport` | 200 | PARTIEL | Idem |
| `/mvp0/workflow/{id}` | 200 | OK | Parcours complet testé |
| `/` | 200 | OK | Simulation + bouton Simuler |
| `/simulation-mrp` | 200 | OK | Charge, onglets visibles |
| `/imports` | 200 | OK | Wizard Excel |
| `/parametres` | 200 | OK | Formules / coeffs |
| `/articles` | 200 | OK | CRUD + onglets |
| `/cbn` | 200 | OK | Calcul besoins (seed DB) |
| `/pegging` | 200 | OK | **Absent du menu** |
| `/aps` | 200 | OK | Hub liens |
| `/aps/journal` | 200 | DÉMO | Filtrer + Appender demo → SQL |
| `/aps/attendus` | 200 | DÉMO | Filtrer + Emitter demo |
| `/aps/referentiel` | 200 | PARTIEL | Lecture seule + seed |
| `/aps/compilateur` | 200 | DÉMO | Recompiler / Invalider |
| `/aps/capacites` | 200 | DÉMO | Calculer + réserve demo |
| `/aps/cbn` | 200 | DÉMO | CBN segments simulé |
| `/aps/flux` | 200 | DÉMO | Recalculer / Publier |
| `/aps/charges` | 200 | DÉMO | Calculer charges |
| `/aps/ctp` | 200 | DÉMO | Évaluer / Promettre / Refuser |
| `/aps/promesses` | 200 | DÉMO | Rafraîchir / Libérer |
| `/aps/contrats` | 200 | DÉMO | Publish demo + alerte zone gelée |
| `/aps/plan-hebdo` | 200 | PARTIEL | Lecture snapshots |
| `/aps/plan-jour` | 200 | PARTIEL | Lecture snapshots |
| `/aps/cycle-nocturne` | **500** | **CASSÉ** | Si `aps_nightly_runs` non vide (DataReader) |
| `/aps/recette` | 200 | DÉMO | Évaluations simulées |
| `/aps/nervosite` | 200 | PARTIEL | Lecture métriques |

---

## 5. Tableau des boutons testés (E2E)

| Page | Bouton(s) | Résultat | SQL / effet observé |
|---|---|---|---|
| MVP-0 | Créer | OK | `mvp0_campaigns` +1 |
| MVP-0 | Parcours demo bout-en-bout | OK | Gate `INSUFFICIENT_DATA` (SIMULATED) |
| MVP-0 | Import seed anomalies | OK | working set + batch |
| MVP-0 | Import seed REAL propre | OK | batch REAL |
| MVP-0 | Validation | OK | `mvp0_anomalies`, `mvp0_validation_runs` |
| MVP-0 | By-pass bloquants | OK | `mvp0_bypasses`, `mvp0_bypass_history` |
| MVP-0 | Score intrant | OK | `mvp0_scores.input_json` |
| MVP-0 | Backtest (sans by-pass) | OK bloqué | Message BLOCKING affiché |
| MVP-0 | Backtest (avec by-pass) | OK | `mvp0_scores.backtest_json` |
| MVP-0 | Avis planificateur | OK | `planner_*` dans `mvp0_scores` |
| MVP-0 | Gate | OK | `gate_outcome` campagne |
| MVP-0 | Rapport | OK | `mvp0_reports` + HTML |
| Simulation `/` | Simuler | OK | Pas d’erreur UI |
| CBN legacy | Calculer les besoins | OK | `cbn_runs` si commandes seed |
| APS Journal | Filtrer / Appender demo | OK | `aps_journal_events` |
| APS Attendus | Filtrer / Emitter demo | OK | `aps_expectations` |
| APS Compilateur | Recompiler / Rafraichir | OK | `aps_compiled_artifacts` |
| APS Capacités | Calculer / Réserver demo | OK | capacité + réservations |
| APS CBN | Lancer CBN APS | OK | `aps_segment_cbn_runs` |
| APS Flux | Recalculer / Publier | OK | tables load/buffer |
| APS Charges | Calculer charges | OK | `aps_load_runs` |
| APS CTP | Évaluer / Promettre / Refuser | OK | `aps_ctp_promises` |
| APS Promesses | Rafraîchir | OK | liste promesses |
| APS Contrats | Publier / Révision | OK + alerte | zone gelée attendue |
| APS Cycle nocturne | Lancer cycle | PARTIEL | cycle enregistré mais page recharge **500** |
| APS Recette | 3 boutons demo | OK | `aps_replay_validations` |

---

## 6. Statut par page (format demandé)

### MVP-0 — Campagnes (`/mvp0`)
- **Statut :** OK
- **Sert à :** Créer/lister campagnes validation (1 famille)
- **L’utilisateur peut :** Créer, demo bout-en-bout, ouvrir workflow
- **Données :** SQL `mvp0_campaigns` ; REAL ou SIMULATED
- **Exemple Montepull :** Famille PULL, site SITE-1
- **Limites :** pas de suppression campagne

### MVP-0 — Workflow (`/mvp0/workflow/{id}`)
- **Statut :** OK (parcours complet validé E2E)
- **Sert à :** Enchaîner imports → validation → scores → gate → rapport
- **SQL vérifié :** campaigns, batches, anomalies, bypasses, scores, reports

### MVP-0 — Sous-urls validation…rapport
- **Statut :** PARTIEL (écran sélecteur, pas de UI dédiée)

### Simulation, Simulation MRP, Imports, Paramètres, Articles, CBN legacy
- **Statut :** OK à PARTIEL (legacy métier fonctionnel sur seed SQL)
- **Données :** tables référentiel / `sim_*` / `cbn_*`

### Pegging (`/pegging`)
- **Statut :** OK mais **NON TESTABLE depuis le menu** (route orpheline)

### Prototype APS (`/aps/*`)
- **Statut global :** DÉMO / EXPÉRIMENTAL
- **Données :** seeds `aps_*`, articles DEMO, pas de GO MVP-0

### Cycle nocturne (`/aps/cycle-nocturne`)
- **Statut :** **CASSÉ** dès qu’un run existe en base
- **Cause :** `ListNightlyRunsAsync` — DataReader ouvert sur la même connexion (ligne 336–353)

---

## 7. Bugs reproductibles

### BUG-1 — Cycle nocturne HTTP 500 (bloquant expérimental)
**Étapes :**
1. Ouvrir `/aps/cycle-nocturne`
2. Cliquer **Lancer cycle** (ou avoir ≥1 ligne dans `aps_nightly_runs`)
3. Recharger la page

**Résultat :** HTTP 500, exception serveur :  
`There is already an open DataReader associated with this Connection which must be closed first.`  
**Fichier :** `SqlServerApsPhase9Repository.cs` → `ListNightlyRunsAsync`

### BUG-2 — App HTTP 500 après reset DB sans redémarrage
**Étapes :** App en cours + `python scripts/init_db.py --reset`  
**Résultat :** `/mvp0` → 500 jusqu’au redémarrage de l’app.

### BUG-3 — Contrats : révision bloquée (comportement attendu mais message fort)
**Alerte UI :** « Zone GELEE : modification interdite » — cohérent métier demo, peut surprendre l’utilisateur.

---

## 8. Erreurs console / réseau / serveur

| Source | Message |
|---|---|
| Serveur | DataReader ouvert (`/aps/cycle-nocturne`) |
| UI APS Contrats | Zone gelée après publish demo |
| UI MVP-0 | « Backtest interdit : anomalies BLOCKING » (comportement **attendu**) |
| Playwright (avant install) | Chromium executable missing — corrigé pour l’audit |

Pas d’erreur JavaScript bloquante observée sur les routes 200.

---

## 9. Vérifications SQL (après E2E)

```text
mvp0_campaigns     : 3
  PULL SIMULATED   → gate INSUFFICIENT_DATA
  AUDIT* REAL      → gate GO
  BLK* SIMULATED   → NEEDS_CORRECTION (backtest bloqué testé)
mvp0_import_batches: 3
mvp0_anomalies     : 30
mvp0_bypasses      : 10
mvp0_reports       : 2
articles (seed)    : 8
aps_journal_events : 19
aps_nightly_runs   : 1 → déclenche BUG-1
```

---

## 10. Données réelles vs simulées

| Zone | Réelles | Simulées / demo |
|---|---|---|
| MVP-0 campagne REAL + seed propre | Import REAL, Gate GO possible | — |
| MVP-0 campagne SIMULATED | — | Gate **INSUFFICIENT_DATA** (validé) |
| Legacy Imports / Articles / CBN | Excel + seed SQL DOUBLYGILF | — |
| Simulation MRP | — | Tables `sim_*` isolées |
| APS prototype | Structure SQL réelle | Contenu DEMO (FIL-DEMO, FC-DEMO, CMD-DEMO…) |

---

## 11. Workflow MVP-0 validé réellement

| Étape | Validé E2E | Preuve |
|---|---|---|
| Création campagne | Oui | Navigation workflow + SQL |
| Import (seed / REAL) | Oui | `mvp0_import_batches` |
| Validation + anomalies | Oui | 30 anomalies en base |
| By-pass + justification | Oui | 10 bypasses |
| Score intrant | Oui | `mvp0_scores` |
| Backtest sans solveur | Oui | REAL workflow complet |
| Backtest bloqué si BLOCKING | Oui | Test dédié OK |
| Score résultat | Oui | Couplé backtest |
| Avis planificateur | Oui | `planner_approved` |
| Gate SIMULATED → pas GO | Oui | `INSUFFICIENT_DATA` |
| Gate REAL → GO possible | Oui | Campagne AUDIT* → `GO` |
| Rapport | Oui | 2 rapports HTML |
| Import CSV preview UI | Oui | Page `/mvp0/imports` |

**Non validé en E2E :** upload fichier CSV réel utilisateur (InputFile) — boutons seed et API OK.

---

## 12. Fonctions présentes mais non pleinement fonctionnelles

- Sous-pages MVP-0 `/validation` … `/rapport` (redirect only)
- `/pegging` (hors menu)
- APS : cycle nocturne (CASSÉ avec historique), plans hebdo/jour vides sans publish contrat
- Import Excel MVP-0 : CSV seulement
- Gate GO « propre » sans by-pass : non rejoué en REAL sur seed anomalies (seed propre utilisé)

---

## 13. Priorités de correction

### Bloquant
1. **BUG-1** DataReader `ListNightlyRunsAsync` → `/aps/cycle-nocturne` inutilisable après 1er run

### Important
2. Redémarrage ou reconnexion SQL après reset DB (doc ops / healthcheck)
3. MVP-0 : pages sous-urls dédiées ou fusionner dans workflow (UX)
4. Ajouter `/pegging` au menu ou lien depuis CBN

### Mineur
5. Messages zone gelée contrats (clarifier c’est voulu)
6. Tests E2E : sérialiser route cycle-nocturne vs bouton « Lancer cycle »

---

## 14. Résumé pour le responsable

L’application **compile**, **116 tests unitaires passent**, et **54/55 tests Playwright** passent sur **toutes les routes** sauf `/aps/cycle-nocturne` (bug SQL DataReader).

Le **MVP-0 est utilisable de bout en bout** : campagne REAL → validation → scores → backtest → avis planificateur → **Gate GO** → rapport. Les campagnes **SIMULATED** obtiennent bien **`INSUFFICIENT_DATA`** (pas de GO). Le backtest est **bloqué** sans by-pass des anomalies BLOCKING.

Les modules **legacy** (Articles, Imports, Paramètres, CBN) **chargent et répondent** sur la base seed.

Les modules **Prototype / Expérimental** fonctionnent en **mode démo** (boutons → écritures `aps_*`) mais **ne doivent pas** servir au GO Gate 0→1. Un bug rend **Cycle nocturne** inaccessible dès qu’un run a été enregistré.

**Recommandation :** poursuivre la validation métier MVP-0 sur données REAL Montepull ; corriger BUG-1 avant de demo APS M7.

---

## 15. Tests Playwright réutilisables

**Projet :** `tests/Axioplan.GammesNomenclatures.E2E.Tests/`

| Fichier | Contenu |
|---|---|
| `RoutesAuditTests.cs` | 33 routes → HTTP 200 |
| `Mvp0WorkflowTests.cs` | Demo SIMULATED, REAL GO, backtest bloqué, imports |
| `LegacyPagesTests.cs` | Simulation, MRP, CBN, pages legacy |
| `ApsPagesTests.cs` | Boutons APS + pages lecture seule |
| `AuditConfig.cs` | URL + SQL |
| `SqlAuditHelper.cs` | Vérifications SQL |

**Lancer :**
```powershell
# Terminal 1
dotnet run --project src/Axioplan.GammesNomenclatures.Web --urls http://localhost:5280

# Terminal 2 (une fois Playwright installé)
dotnet test tests/Axioplan.GammesNomenclatures.E2E.Tests/
```

**Prérequis Playwright (une fois) :**
```powershell
powershell -ExecutionPolicy Bypass -File tests/Axioplan.GammesNomenclatures.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

---

## 16. Tableau récapitulatif final

| Onglet | Sert réellement à quoi | Statut | Données | À utiliser maintenant ? |
|---|---|---|---|---|
| MVP-0 | Gate 0→1 fiabilisation | OK | SQL mvp0_* | **Oui** |
| Simulation | Voir gammes sim MRP | OK | sim_* | Bac à sable |
| Simulation MRP | MRP complet simulé | OK | sim_* | Bac à sable |
| Imports | Excel → référentiel | OK | SQL réel | Oui |
| Paramètres | Formules / coeffs | OK | SQL réel | Oui |
| Articles | CRUD articles | OK | SQL réel | Oui |
| CBN legacy | Besoins nets | OK | SQL seed | Oui |
| Pegging | Pegging CBN | OK (hors menu) | SQL | Via URL |
| Hub + APS exp. | Labo APS | DÉMO / PARTIEL | Seeds aps_* | Non pour GO |
| Cycle nocturne | M7 manuel | **CASSÉ** | aps_nightly_* | Non |
