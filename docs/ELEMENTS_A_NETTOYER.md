# ELEMENTS_A_NETTOYER

> Phase 2 — aucune suppression effectuée. Recommandations pour phases ultérieures.

---

## 1. CBN legacy (`/cbn/legacy`)

| Critère | Détail |
|---------|--------|
| **Pourquoi** | Triple implémentation CBN (legacy, Simulation MRP, APS). |
| **Dépendances** | `CbnService`, `CbnEngine`, tables `cbn_*`, tests Domain + E2E `/cbn/legacy`. |
| **Recommandation** | **Masquer** (fait via hub) → **Archiver** après migration données → **Supprimer** quand Simulation MRP + APS couvrent le métier. |

---

## 2. Simulation gammes (`/`)

| Critère | Détail |
|---------|--------|
| **Pourquoi** | Doublon partiel avec Simulation MRP ; hors menu principal. |
| **Dépendances** | `GammesSimulationService`, route `/`, E2E. |
| **Recommandation** | **Conserver** — atelier legacy ; **Masquer** (fait) ; fusion future avec Simulation MRP. |

---

## 3. ConsultationService (sans UI)

| Critère | Détail |
|---------|--------|
| **Pourquoi** | Repository + service enregistrés, aucune page Blazor. |
| **Dépendances** | `SqlServerConsultationRepository`, DI Infrastructure. |
| **Recommandation** | **Conserver** court terme ; créer page ou **Supprimer** si non planifié. |

---

## 4. Module Python `axioplan/` + `planning_ai`

| Critère | Détail |
|---------|--------|
| **Pourquoi** | Double stack CBN/pegging avec C# Domain. |
| **Dépendances** | `tests/test_*.py`, `scripts/init_db.py`. |
| **Recommandation** | **Conserver** (interdit phase 2) ; **Archiver** après parity C# validée. |

---

## 5. Pages APS expérimentales

Compilateur, CTP, Promesses, Contrats, Recette, Cycle nocturne, Nervosité, Plan hebdo/jour.

| Critère | Détail |
|---------|--------|
| **Pourquoi** | Squelettes ou données demo (`TO_CONFIRM`, seeds). |
| **Dépendances** | Tables `aps_*`, moteurs Domain, E2E `ApsPagesTests`. |
| **Recommandation** | **Masquer** (fait — Outils expérimentaux) ; compléter ou **Supprimer** par module. |

---

## 6. Hub APS (`/aps`)

| Critère | Détail |
|---------|--------|
| **Pourquoi** | Redondant avec nouveau menu + Outils expérimentaux. |
| **Dépendances** | `ApsHub.razor`, liens internes. |
| **Recommandation** | **Masquer** (fait) ; **Rediriger** vers `/admin/experimental` plus tard. |

---

## 7. Mvp0Sections (sélecteur campagne)

| Critère | Détail |
|---------|--------|
| **Pourquoi** | Écran intermédiaire peu utile si redirection workflow. |
| **Dépendances** | Routes `/mvp0/validation` … `/mvp0/rapport`, E2E. |
| **Recommandation** | **Conserver** routes ; **Rediriger** auto (fait si 1 campagne). |

---

## 8. Docs datées

`docs/ETAT_PROJET.md`, `docs/FINAL_PROJET.md` — contenu partiellement obsolète.

| Recommandation | **Conserver** historique ; référence = `DOCUMENTATION_COMPLETE_APPLICATION.md` + `REWORK_INTERFACE.md`. |

---

## 9. Boutons « demo » APS

Reserve demo, Emit demo, seeds MVP-0, etc.

| Recommandation | **Conserver** phase actuelle ; **Masquer** en production Montepull quand données réelles branchées. |

---

## Synthèse actions Phase 2

| Action | Éléments |
|--------|----------|
| **Masqué navigation** | APS expérimental, Simulation, CBN legacy direct, hub APS |
| **Conservé** | Toutes routes, tables, moteurs |
| **Supprimer** | Rien (phase ultérieure uniquement) |
