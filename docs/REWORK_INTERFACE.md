# REWORK_INTERFACE — Phase 2

> Date : 22 juillet 2026  
> Objectif : simplifier navigation et présentation sans modifier moteurs, formules ni schémas SQL.

---

## 1. Ancienne organisation

- **Menu horizontal** en haut (`topbar`) avec 10+ liens visibles.
- Dropdown **Articles** (4 colonnes) uniquement sur la liste.
- Dropdown **Prototype / Expérimental** regroupant tout l’APS (15+ liens).
- **MVP-0**, **Simulation**, **Simulation MRP** au même niveau que le métier.
- **CBN legacy** (`/cbn`) accessible comme entrée isolée (retiré du menu en session précédente).
- Pas de tableau de bord.
- Capacités / Charges / Flux en pages APS séparées dans le menu expérimental.

---

## 2. Nouvelle organisation (5 zones)

| Zone | Entrées menu |
|------|----------------|
| **Accueil** | Tableau de bord (`/accueil`) |
| **Référentiels** | Articles, Nomenclatures et gammes, Stock, Paramètres |
| **Préparation des données** | Imports, Qualité des données — MVP-0 |
| **Calculs et planification** | CBN, Pegging, Charges et capacités, Planification APS |
| **Administration / Avancé** | Journal, Attendus, Outils expérimentaux, Documentation technique |

**Layout** : menu latéral gauche sobre (`sidebar`), contenu à droite.

---

## 3. Pages déplacées / masquées

| Ancien accès menu | Nouveau accès |
|-------------------|---------------|
| MVP-0 | Préparation → Qualité des données — MVP-0 |
| Simulation `/` | Masqué — lien accueil secondaire + Outils expérimentaux |
| Simulation MRP | Masqué — via CBN → Simulation ou Outils expérimentaux |
| CBN legacy direct | Calculs → CBN → Données réelles → `/cbn/legacy` |
| Hub APS + 15 pages | Admin → Outils expérimentaux (badge Expérimental) |
| Capacités / Charges / Flux | Calculs → Charges et capacités (onglets) |
| Planification | Calculs → Planification APS |

---

## 4. Routes conservées (compatibilité)

Toutes les routes listées dans `RoutesAuditTests.AllRoutes` retournent **HTTP 200**.

| Route | Comportement |
|-------|--------------|
| `/cbn` | Hub CBN unifié (nouveau) |
| `/cbn/legacy` | Ancien écran CBN métier |
| `/cbn/simulation`, `/cbn/aps` | Modes du hub |
| `/nomenclatures-gammes` | Redirection → `/articles/bom` |
| `/mvp0/validation` … `/mvp0/rapport` | Redirection auto si 1 campagne, sinon sélecteur |
| `/aps/capacites`, `/aps/charges`, `/aps/flux` | Pages inchangées (panels réutilisés) |
| `/` | Simulation gammes (inchangé) |

---

## 5. Nouvelles routes

- `/accueil` — tableau de bord
- `/charges-capacites`, `/charges-capacites/{tab}`
- `/cbn/legacy`, `/cbn/simulation`, `/cbn/aps`
- `/admin/experimental`, `/admin/documentation`
- `/aps/resultats`
- `/imports/bom`, `/imports/autres`
- `/parametres/{tab}` (coefficients, pertes, cbn, general)

---

## 6. Composants créés

| Composant | Rôle |
|-----------|------|
| `PageHeader` | Titre + description |
| `StatusBadge` | Opérationnel / Partiel / Expérimental / Données manquantes |
| `ModuleCard` | Carte tableau de bord |
| `SectionTabs` | Onglets de navigation cohérents |
| `ArticleSectionTabs` | Sous-nav Articles |
| `AlertPanel` | Messages info / warning |
| `LoadingState` | Chargement |
| `EmptyState` | État vide |
| `Mvp0ProgressBar` | 10 étapes MVP-0 |
| `ApsCapacitesPanel` | Contenu capacités (réutilisable) |
| `ApsChargesPanel` | Contenu charges |
| `ApsFluxPanel` | Contenu flux / saturation |

---

## 7. Choix UX

1. **Masquer plutôt que supprimer** — prototypes dans Outils expérimentaux.
2. **Un seul CBN visible** — 3 moteurs conservés, hub unique.
3. **Parcours charges unifié** — 4 onglets, mêmes services APS.
4. **MVP-0 renommé** — « Qualité des données — MVP-0 » + barre de progression.
5. **Pas de fausses statistiques** sur le tableau de bord — statuts qualitatifs uniquement.
6. **Design sobre** — fond clair, cartes simples, pas de librairie UI externe.

---

## 8. Résultats des tests

| Suite | Résultat |
|-------|----------|
| **Domain.Tests** | ✅ 117 / 117 passés |
| **E2E.Tests** | ✅ 101 / 101 passés (dont `RoutesAuditTests` + `NavigationReworkTests`) |
| **Build solution** | ✅ OK |

Tests E2E ajoutés : `NavigationReworkTests.cs` (menu, accueil, CBN, charges, articles, legacy CBN).

---

## 9. Captures d’écran

Voir `docs/screenshots/phase2/` :

- `01-accueil.png`
- `02-menu-sidebar.png` (inclus dans accueil)
- `03-mvp0.png`
- `04-cbn.png`
- `05-charges-capacites.png`
- `06-planification.png`
- `07-articles.png`
- `08-imports.png`

---

## 10. Fichiers modifiés (liste)

### Layout & styles
- `Components/Layout/MainLayout.razor`
- `wwwroot/app.css`

### Composants partagés (nouveaux)
- `Components/Shared/PageHeader.razor`
- `Components/Shared/StatusBadge.razor`
- `Components/Shared/ModuleCard.razor`
- `Components/Shared/SectionTabs.razor` + `SectionTabItem.cs`
- `Components/Shared/ArticleSectionTabs.razor`
- `Components/Shared/AlertPanel.razor`
- `Components/Shared/LoadingState.razor`
- `Components/Shared/EmptyState.razor`
- `Components/Shared/Mvp0ProgressBar.razor`
- `Components/Shared/ModuleStatusKind.cs`
- `Components/Shared/ApsCapacitesPanel.razor`
- `Components/Shared/ApsChargesPanel.razor`
- `Components/Shared/ApsFluxPanel.razor`

### Pages (nouvelles)
- `Components/Pages/Home.razor`
- `Components/Pages/ChargesCapacites.razor`
- `Components/Pages/CbnLegacy.razor`
- `Components/Pages/NomenclaturesGammes.razor`
- `Components/Pages/AdminExperimental.razor`
- `Components/Pages/AdminDocumentation.razor`
- `Components/Pages/ApsResultats.razor`

### Pages (modifiées)
- `Components/Pages/Cbn.razor` (hub)
- `Components/Pages/Articles.razor`
- `Components/Pages/Imports.razor`
- `Components/Pages/Parametres.razor`
- `Components/Pages/Stock.razor`
- `Components/Pages/ApsCapacites.razor`
- `Components/Pages/ApsCharges.razor`
- `Components/Pages/ApsFlux.razor`
- `Components/Pages/ApsPlanification.razor`
- `Components/Pages/Mvp0/Mvp0Home.razor`
- `Components/Pages/Mvp0/Mvp0Sections.razor`

### Tests
- `tests/.../RoutesAuditTests.cs`
- `tests/.../NavigationReworkTests.cs` (nouveau)

### Documentation
- `docs/REWORK_INTERFACE.md` (ce fichier)
- `docs/ELEMENTS_A_NETTOYER.md`
