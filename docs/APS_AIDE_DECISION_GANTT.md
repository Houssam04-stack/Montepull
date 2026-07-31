# Aide à la décision APS — durées & Gantt

## Objectif

Fournir au planificateur Montepull une **planification exploitable** après chaque calcul APS :

- durée prévisionnelle (jours ouvrés), dates début/fin, marge vs date besoin ;
- taux de charge maximal (ρ) et ressource goulot ;
- chemin critique (OF + opérations sur le goulot) ;
- diagramme de Gantt (OF, opérations, ressources, dépendances, aujourd’hui, retards) ;
- exports CSV, Excel, SVG, PNG, PDF (impression navigateur).

Ce n’est **pas** une estimation isolée : les tâches sont dérivées des **buckets de charge** déjà calculés par `ApsFluxService` / `LoadEngine`.

## Où l’utiliser

| Écran | Route | Comportement |
|-------|-------|--------------|
| Planification APS | `/aps/planification` | Régénéré après « Lancer planification complète » |
| Résultats | `/aps/resultats` | Régénéré à la sélection d’un run publié |
| Flux / saturation | `/aps/flux`, `/charges-capacites/flux` | Régénéré après Recalculer / Publier |

## Architecture

```
ApsPlanningService / ApsFluxService
        │
        ▼
ApsScheduleDecisionEngine (tâches / durée / marge)
        │
        ▼
ApsGanttLayoutEngine (échelle, ticks, barres min, SVG écran + export)
        │
        ▼
ApsDecisionGanttPanel (colonne gauche fixe + timeline scrollable + exports)
```

Écran : zone gauche sticky (OF / op / qté / statut) + SVG timeline seul.  
Exports SVG/PNG/PDF : SVG complet avec libellés, styles embarqués, sans menu.

Impression PDF : fenêtre dédiée, `@page A4 landscape`, classes `no-print` / `gantt-print-container`.


## Fichiers principaux

- `Domain/Aps/Schedule/ApsScheduleDecisionModels.cs`
- `Domain/Aps/Schedule/ApsScheduleDecisionEngine.cs`
- `Application/Aps/ApsScheduleDecisionService.cs`
- `ApsDecisionGanttPanel` (paramètres `Schedule`, `PanelTitle`, `GanttSvgId`)
- `Web/wwwroot/js/axioplan-export.js`
- `Web/wwwroot/app.css` (styles KPI / Gantt)
- Tests : `tests/.../ApsScheduleDecisionEngineTests.cs`

## Règles métier (résumé)

1. **OF** : une barre parent par demande / article fini, span = min→max des buckets internes.
2. **Opérations** : un bucket de charge = une opération sur sa ressource, avec dépendances OF + séquence ressource + enchaînement inter-ressources (tricot → remail → …).
3. **Chemin critique** : OF + opérations de la ressource goulot (sinon toutes les opérations chronologiques).
4. **Jours ouvrés** : lun–ven inclus.
5. **Marge** : jours ouvrés entre fin estimée et date besoin (négative si retard).
6. **Statuts couleurs** : planifié, en cours, terminé, retard, saturé.

## Exports

| Format | Méthode |
|--------|---------|
| CSV | Sérialisation C# des tâches |
| Excel | SpreadsheetML (`.xls`) + feuille Synthèse |
| SVG | Export du SVG Gantt |
| PNG | Rasterisation canvas côté navigateur |
| PDF | Fenêtre d’impression dédiée (Enregistrer en PDF) |

## MVP-0 Imprimer / PDF

Le bouton **Imprimer / PDF** du parcours `/mvp0/workflow/{id}` appelle `axioplan.printElementById` sur l’iframe du rapport (plus un stub).
