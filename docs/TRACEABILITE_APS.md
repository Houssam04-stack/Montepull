# Matrice de traçabilité APS — Montepull

**Date :** 22 juillet 2026  
**Sources :**
- `docs/_extract/dossier_aps_conception.txt` — Dossier APS conception (juillet 2026)
- `docs/_extract/aps_axioplan_cahier_des_charges.txt` — Cahier des charges Axioplan APS v1

---

## Périmètre et interprétation

### Périmètre actuel (v1)

MVP-0 (dossier §5–6) **plus** cahier §13.1 étapes 0–8 :

| Bloc | Contenu |
|------|---------|
| Journal | Faits append-only + attendus immuables |
| Référentiel | Organisation, ressources, temps, produits, compétences, maintenance (socle démo + seed) |
| Compilateur | Artefacts compilés déterministes (needs, HRE, loads, lead times) |
| Capacité nette | Cascade cinq étages auditables (brute → ouverte → effective → réserve → nette) |
| CBN segments | Explosion/netting par segment, découplage tricoté/assemblage |
| Charge / ρ / goulot | Calcul charge HRE, saturation ρ, identification goulot |
| CTP | Promesse capacitaire à sept champs (date, régime, goulot, fiabilité, HRE, densité, float) |
| Contrats flux | Contrats hebdomadaires immuables, barrières temporelles, coût d'inertie |
| M7 recalibration | Cycle nocturne démo (EWMA estimateurs, recette, nervosité) |

### Hors périmètre (v1)

| Référence | Justification |
|-----------|---------------|
| Dossier §7 MVP-1 | Ordonnancement fini, Gantt, CP-SAT — conditionné Gate GO |
| Dossier §8 MVP-2 | Planification niveau 1 / S&OP agrégé |
| Cahier §13.1 #9 | M2 MILP enveloppe |
| Cahier §13.1 #10 | M6 métaheuristique |
| Cahier §13.1 #11 | Phase 2 couche événementielle (abonnés temps réel) |
| Transversal | Intégration ERP/MES temps réel, multi-site, authentification |

---

## Légende des statuts

| Statut | Signification |
|--------|---------------|
| **Implémentée** | Exigence couverte par du code exécutable et testé |
| **Partielle** | Fondations ou démo présentes ; couverture fonctionnelle incomplète |
| **Absente** | Dans le périmètre v1 mais non livrée |
| **Hors périmètre** | Explicitement exclue de v1 (cf. tableau ci-dessus) |

---

## Matrice de traçabilité

| Référence | Exigence | Statut | Fichiers concernés | Tests associés | Modifications réalisées |
|-----------|----------|--------|-------------------|----------------|-------------------------|
| **Dossier §2.0 — Référentiel** |||||
| §2.0.a | Modèle organisationnel : Compagnie → Site → Usine → Zone → Emplacement | Partielle | `src/.../Domain/Aps/Referential/ApsReferentialEnums.cs`, `src/.../Infrastructure/Repositories/SqlServerApsReferentialRepository.cs`, `database/seed/001_referentials.sql`, `src/.../Web/Components/Pages/ApsReferential.razor` | `ApsSqlIntegrationTests.Places_are_recursive_and_stock_lot_has_bath_status` | — |
| §2.0.a | Tiers clients/fournisseurs avec rôles et catégories structurantes | Absente | `ApsReferentialEnums.cs` (types partiels) | — | — |
| §2.0.b | Postes de charge, centres de charge, ressources (pools statiques/dynamiques) | Partielle | `SqlServerApsReferentialRepository.cs`, `SqlServerApsCapacityRepository.cs`, `src/.../Domain/Aps/Capacity/NetCapacityEngine.cs` | `NetCapacityEngineTests.*` | — |
| §2.0.b | Ressources humaines : compétences, certifications, plafond 2 288 h | Absente | — | — | — |
| §2.0.c | Calendriers, plages horaires, équipes, exceptions (fériés, arrêts) | Partielle | `SqlServerApsCapacityRepository.cs`, seed référentiel | `NetCapacityEngineTests.Five_stages_and_reserve_deduction` | — |
| §2.0.d | Articles typés (composant, composé, PF, service, outil, machine) | Partielle | `src/.../Domain/Articles/*`, `src/.../Application/Models/ArticleModels.cs`, `Mvp0ApsBridgeMapper.cs` | `Mvp0ApsBridgeMapperTests.MapArticleType_*` | Mapping types MVP-0 → APS via `Mvp0ApsBridgeMapper` |
| §2.0.d | Attributs article et familles (regroupement campagnes) | Partielle | `ArticleConfiguratorEngine.cs`, `AttributeValueFormatter.cs`, `SimulationDuplicationEngine` | `SimulationArticleNamingTests`, `FormulaEngineTests` | — |
| §2.0.d | Nomenclatures multi-niveaux avec rebut et pegging opération | Implémentée | `src/.../Domain/CbnEngine.cs`, `src/.../Domain/Aps/SegmentCbn/SegmentCbnEngine.cs`, `PeggingService.cs` | `CbnEngineTests`, `SegmentCbnAndYarnAtpTests`, `DomainEngineTests.PeggingEngineTests` | — |
| §2.0.d | Gammes : opérations, temps cycle/setup, variantes alternatives | Partielle | `src/.../Domain/Aps/Compiler/ApsCompilerModels.cs`, `ApsCompilerEngine.cs`, `PantalonGammeTemplate.cs` | `ApsCompilerEngineTests.CompileSkeleton_*` | — |
| §2.0.e | Référentiel compétences et certifications par opération/RH | Absente | — | — | — |
| §2.0.f | Plans de maintenance préventive → fenêtres indisponibilité | Absente | — | — | — |
| **Dossier §2.1–2.10 — Modules fonctionnels** |||||
| §2.1 | Planification demande niveau 1 : prévisions, profil 25/75, bornes | Hors périmètre | — | — | MVP-2 (dossier §8) |
| §2.1 | Distinction ferme / prévisionnel sur carnet | Partielle | `Mvp0WorkflowService.cs` (carnet passé démo), `ApsCtpModels.cs` (régime MTO/MTS) | `CtpPhase8Tests.Regime_specificity_article_client_wins` | — |
| §2.2 | Explosion BOM multi-niveaux, besoins nets/bruts, rebut | Implémentée | `CbnEngine.cs`, `SegmentCbnEngine.cs`, `SimulationCbnEngine` | `CbnEngineTests`, `SegmentCbnAndYarnAtpTests.Yield_not_applied_twice_*` | — |
| §2.2 | Besoins appro décalés (fil teint 5 sem.) | Partielle | `SegmentCbnEngine.cs`, `ApsCtpService.cs` (lead fil) | `CtpPhase8Tests.R3_respects_supplier_lead` | — |
| §2.2 | Contraintes lots composants (MOQ, multiple, lot-for-lot) | Partielle | `SimulationCbnEngine`, `SimulationEnums.cs` (`SimulationLotRules`) | `SimulationCbnEngineTests` | — |
| §2.2 | Cohérence de bain (dye-lot fil acheté) | Partielle | `SegmentCbnEngine.cs` (ATP fil), `ApsCtpService.cs` | `SegmentCbnAndYarnAtpTests.Yarn_atp_uses_max_not_sum`, `CtpPhase8Tests.Yarn_uses_compatible_bath_code` | — |
| §2.2 | Instanciation cheminement OF (pré/post client) | Partielle | `Phase9Engines.cs` (`ConfigClientOpération` concept), `ApsExpectationService` | `ApsExpectationGrainRulesTests` | — |
| §2.3 | Ordonnancement capacité finie bidirectionnel (ASAP/ALAP) | Hors périmètre | — | — | MVP-1 (dossier §7) — pas de solveur séquencement |
| §2.3 | Séquencement par ressource (pool postes aptes) | Hors périmètre | — | — | MVP-1 |
| §2.3 | Contraintes gamme : précédence, chevauchement, setups séquence-dépendants | Hors périmètre | — | — | MVP-1 |
| §2.3 | Regroupement campagnes (finition, type maille, machine) | Hors périmètre | — | — | MVP-1 / v2 |
| §2.3 | Routes et opérations alternatives (make/buy, segment alternatif) | Partielle | `ApsCtpService.cs`, `NetCapacityEngine.cs` (capacité externe) | `CtpPhase8Tests.Subcontract_when_engagement_compatible`, `NetCapacityEngineTests.External_without_engagement_is_zero` | — |
| §2.4 | Suivi en-cours par emplacement, stock anticipation, tampons min/max | Partielle | `ApsFluxService.cs`, `SqlServerApsFluxRepository.cs`, `Phase9Engines.cs` | `FluxPhase7Tests` (buffers), `Phase9ContractsTests` | — |
| §2.4 | Couverture jours (loi de Little) | Absente | — | — | — |
| §2.5 | CTP : date au plus tôt contre plan chargé | Implémentée | `src/.../Domain/Aps/Ctp/CtpEngine.cs`, `ApsCtpService.cs`, `ApsCtp.razor` | `CtpPhase8Tests.*` (18 tests) | Gate REAL via `Mvp0ApsGateGuard` sur `EvaluateAsync`/`PromiseAsync` |
| §2.5 | Test faisabilité date souhaitée imposée | Implémentée | `CtpEngine.cs`, `ApsCtpService.cs` | `CtpPhase8Tests.First_feasible_date_by_iteration`, `CtpPhase8Tests.Evaluation_does_not_set_reserves` | — |
| §2.5 | Prise en compte stock découplage et étapes pré/post | Partielle | `CtpEngine.cs`, `SegmentCbnEngine.cs` | `CtpPhase8Tests.Reserve_mts_not_silently_*`, `SegmentCbnAndYarnAtpTests.Reserve_mts_not_consumed_silently_by_mto` | — |
| §2.6 | Simulation what-if (commande urgente, panne, retard fil) | Partielle | `Simulation.razor`, `SimulationCbnEngine`, `ApsPlanification.razor` (mode démo) | `SimulationCbnEngineTests`, `ApsPagesTests` | — |
| §2.6 | Replanification événementielle automatique | Hors périmètre | — | — | Phase 2 (cahier §13.1 #11) |
| §2.7 | Contrat production par OF : besoins bruts, charges, cheminement daté | Partielle | `Phase9Engines.cs` (`FlowContractEngine`), `ApsPhase9Services.cs`, `ApsContrats.razor` | `Phase9ContractsTests.*` | — |
| §2.7 | Configuration client pré/post datée (obligatoire/facultatif) | Absente | — | — | — |
| §2.8 | Suivi réalisation prévu/réel, mesure OTIF | Partielle | `Mvp0BacktestReliabilityEngine` (MVP-0), `ExpectationFactMatcher` (Phase 9) | `Mvp0Tests.Backtest_without_solver_computes_gaps`, `Phase9ContractsTests` | — |
| §2.8 | Remontée écarts pour replanification | Hors périmètre | — | — | Phase 2 |
| §2.9 | Cartographie flux en graphe (5 flux : physique, info, décision, doc, qualité) | Partielle | `src/.../Domain/Aps/Flux/FluxEngines.cs`, `ApsFluxService.cs`, `ApsFluxPanel.razor` | `FluxPhase7Tests.*` | — |
| §2.9 | Graphe générique vs instancié par OF | Partielle | `ApsCompilerEngine` (générique), `ApsFluxService` (runs publiés) | `ApsCompilerEngineTests` | — |
| §2.10 | Validation à l'import (intégrité, précédence, temps plausibles) | Implémentée | `Mvp0Engines.cs`, `Mvp0WorkflowService.cs`, `ImportService.cs`, `BomImportEngine` | `Mvp0Tests.Routing_cycle_*`, `Mvp0Tests.Bom_cycle_detected`, `BomImportEngineTests` | — |
| §2.10 | Indice fiabilité intrant (validation, complétude, fraîcheur) | Implémentée | `Mvp0Engines.cs` (`Mvp0InputReliabilityEngine`), `Mvp0WorkflowService.cs` | `Mvp0Tests.Input_score_deterministic_and_explicable`, `Mvp0Tests.Freshness_bands` | — |
| §2.10 | By-pass traçable historisé (qui/quand/quoi/pourquoi) | Implémentée | `Mvp0WorkflowService.cs`, `SqlServerMvp0Repository.cs` (`mvp0_bypasses`, `mvp0_bypass_history`) | `Mvp0Tests.Bypass_requires_justification_and_keeps_anomaly` | — |
| §2.10 | Boucle écarts prévu/réel → auto-apprentissage gammes | Partielle | `RecalibrationEngine` (EWMA démo), `ApsPhase9Services.cs` | `Phase9ContractsTests.Recalibration_threshold_behavior` | — |
| §2.10 | Génération paramétrique gammes depuis attributs | Partielle | `ArticleConfiguratorEngine.cs`, `PantalonGammeTemplate.cs` | `FormulaEngineTests` | — |
| **Dossier §5 — MVP-0** |||||
| §5.1 | Mesurer fiabilité réelle d'une seule famille (données SI) | Implémentée | `Mvp0WorkflowService.cs`, `Mvp0/Mvp0Workflow.razor` | `Mvp0Tests.Campaign_carries_single_family_concept`, `Mvp0WorkflowTests` (E2E) | — |
| §5.2 | Import famille : articles, nomenclature, gammes, calendriers/TRS, carnet passé | Implémentée | `Mvp0CsvImport.cs`, `Mvp0ExcelCsvConverter.cs`, `Mvp0WorkflowService.cs` | `Mvp0Tests.Csv_reject_blank_never_silent_and_auto_map`, `Mvp0Tests.Import_reject_never_silent` | — |
| §5.3 | Règles validation (temps, centre, précédence, cohérence gamme↔BOM) | Implémentée | `Mvp0Engines.cs` (`Mvp0ValidationEngine`) | `Mvp0Tests.*` (cycles, temps, liaisons) | — |
| §5.3 | Indice fiabilité intrant | Implémentée | `Mvp0InputReliabilityEngine` | `Mvp0Tests.Input_score_deterministic_and_explicable` | — |
| §5.3 | By-pass traçable | Implémentée | `Mvp0WorkflowService.CreateBypassAsync` | `Mvp0Tests.Bypass_requires_justification_*` | — |
| §5.3 | Backtest léger prévu/réel (dates, durées, quantités) | Implémentée | `Mvp0BacktestReliabilityEngine` | `Mvp0Tests.Backtest_without_solver_computes_gaps` | — |
| §5.5 | Sorties : indice par type, écarts, journal by-pass, rapport go/no-go | Implémentée | `Mvp0WorkflowService.BuildReportHtml`, pages `/mvp0/*` | `RoutesAuditTests` (`/mvp0/fiabilite`, `/mvp0/backtest`, `/mvp0/rapport`) | — |
| §5.2 HORS | Pas de solveur, Gantt, CTP, ordonnancement en MVP-0 pur | Implémentée | Séparation MVP-0 / APS via `Mvp0ApsGateGuard` | `Mvp0ApsGateGuardTests` | Garde REAL explicite ; démo bypass autorisé |
| **Dossier §6 — Gate 0→1** |||||
| §6 | Indice fiabilité intrant ≥ seuil → GO | Implémentée | `Mvp0GateEngine`, `Mvp0WorkflowService.EvaluateGateAsync` | `Mvp0Tests.Pure_go_when_thresholds_met_no_bypass`, `Mvp0Tests.Go_with_reservations_possible` | — |
| §6 | Écart prévu/réel acceptable ou explicable | Implémentée | `Mvp0GateEngine` (backtest P90, MAPE) | `Mvp0Tests.Blocking_forbids_go_and_planner_required` | — |
| §6 | Planificateur engagé (avis requis) | Implémentée | `Mvp0WorkflowService.RecordPlannerReviewAsync` | `Mvp0Tests.Blocking_forbids_go_and_planner_required` | — |
| §6 | Outcomes GO / GO_WITH_RESERVATIONS / NO_GO | Implémentée | `Mvp0GateOutcomes`, `Mvp0ApsGateGuard.cs` | `Mvp0ApsGateGuardTests.IsApproved_*` | `Mvp0ApsGateGuard` bloque APS REAL si gate non GO |
| **Dossier §7 — MVP-1 (hors périmètre v1)** |||||
| §7.1 | Ordonnanceur capacité finie deux goulots (tricotage, assemblage) | Hors périmètre | — | — | Conditionné Gate GO ; CP-SAT/heuristique non livré |
| §7.3 | Séquencement fini au plus tôt/au plus tard, précédence, cadence | Hors périmètre | — | — | MVP-1 |
| §7.4 | Sortie Gantt par ressource | Hors périmètre | — | — | MVP-1 |
| §7.4 | CTP fiable par OF post-ordonnancement | Partielle | `ApsCtpService.cs` (CTP capacitaire sans séquencement fin) | `CtpPhase8Tests` | CTP v1 sans ordonnancement fini |
| §7.4 | OTIF prédit, charge goulot > 90 %, journal dérogations | Partielle | `ApsFluxService.cs` (ρ, goulot), `Phase9Engines.cs` (dérogations modèle) | `FluxPhase7Tests`, `Phase9ContractsTests` | ρ/goulot sans OTIF prédit complet |
| §7.6 | Solveur CP-SAT (OR-Tools) ou heuristique liste | Hors périmètre | — | — | MVP-1 Annexe A |
| **Dossier §8 — MVP-2 (hors périmètre v1)** |||||
| §8 | Planification niveau 1 : S&OP, prévisions 25/75, réservations capacité | Hors périmètre | — | — | MVP-2 / Annexe B |
| §8 | Génération bornes (fenêtres, tampons) pour niveau 2 | Hors périmètre | — | — | MVP-2 |
| §8 | Moteur prévisions SARIMA / analogie famille | Hors périmètre | — | — | Annexe C — MVP-2 |
| **Cahier §13.1 — Ordre de construction (étapes 0–8)** |||||
| §13.1 #0 | Journal append-only des faits (horodaté, immuable) | Implémentée | `ApsJournalService`, `SqlServerApsJournalRepository.cs`, `ApsJournalModels.cs`, `ApsJournal.razor` | `ApsSqlIntegrationTests.Journal_append_only_preserves_occurred_vs_recorded` | — |
| §13.1 #0 | Journal des attendus (immuable, replan = nouvel expected_id) | Implémentée | `ApsExpectationService`, `SqlServerApsExpectationRepository.cs`, `ApsAttendus.razor` | `ApsSqlIntegrationTests.Expectation_is_immutable_and_replan_creates_new_id` | — |
| §13.1 #1 | Référentiel + relevé terrain | Partielle | `ApsReferentialService`, `SqlServerApsReferentialRepository.cs`, seed SQL | `ApsSqlIntegrationTests.Places_are_recursive_*` | — |
| §13.1 #2 | Compilateur → artefacts needs/HRE/loads/lead times | Implémentée | `ApsCompilerEngine.cs`, `ApsCompilerService`, `ApsCompilateur.razor` | `ApsCompilerEngineTests.Hash_is_deterministic`, `ApsSqlIntegrationTests.Compiler_invalidates_and_rebuilds` | — |
| §13.1 #3 | Capacité nette — cinq étages auditables | Implémentée | `NetCapacityEngine.cs`, `ApsCapacityService`, `ApsCapacites.razor` | `NetCapacityEngineTests.Five_stages_and_reserve_deduction` | — |
| §13.1 #4 | CBN segments + ATP fil non additif | Implémentée | `SegmentCbnEngine.cs`, `ApsCapacityCbnServices.cs`, `ApsCbn.razor` | `SegmentCbnAndYarnAtpTests.*` | Cascade segments intégrée à `ApsPlanningService` |
| §13.1 #5 | Charge + ρ + identification goulot | Implémentée | `LoadEngine` (`FluxEngines.cs`), `BottleneckEngine`, `ApsFluxService.cs`, `ApsCharges.razor`, `ApsResultats.razor` | `FluxPhase7Tests.*`, E2E `/aps/resultats` | Page `/aps/resultats` — historique runs publiés |
| §13.1 #6 | CTP à sept champs + réservation promesse (P7) | Implémentée | `CtpEngine.cs`, `ApsCtpService.cs`, `ApsCtp.razor`, `ApsPromesses.razor` | `CtpPhase8Tests.Same_input_same_output`, `CtpPhase8Tests.Promise_flag_sets_reserves` | Gate REAL sur CTP (`Mvp0ApsGateGuard`) |
| §13.1 #7 | Contrats de flux + buffers + barrières temporelles | Partielle | `Phase9Engines.cs`, `ApsPhase9Services.cs`, `ApsContrats.razor` | `Phase9ContractsTests.Published_contract_immutable`, `Phase9ContractsTests.Frozen_zone_forbids_modification` | — |
| §13.1 #8 | M7 recalibration — cycle nocturne, EWMA, recette | Partielle | `RecalibrationEngine`, `ApsPhase9Services.cs`, `ApsCycleNocturne.razor`, `ApsRecette.razor` | `Phase9ContractsTests.Nightly_steps_order_fixed`, `Phase9ContractsTests.Recipe_states_and_m2_lock` | Recette exécutable en démo ; pas encore sur historique production réel |
| **Cahier §13.2 — Décisions non rétro-installables** |||||
| §13.2 #1 | Journal append-only faits | Implémentée | `ApsJournalModels.cs`, `SqlServerApsJournalRepository.cs` | `ApsSqlIntegrationTests.Journal_append_only_*` | — |
| §13.2 #2 | Unité charge homogène HRE | Implémentée | `LoadEngine`, `FluxEngines.cs`, `ApsCapacityUnits.LabourHours` | `FluxPhase7Tests.Labour_load_in_person_hours` | — |
| §13.2 #3 | Cascade capacité cinq étages auditables | Implémentée | `NetCapacityEngine.cs` | `NetCapacityEngineTests.Five_stages_and_reserve_deduction` | — |
| §13.2 #4 | Réservation à la promesse (P7) | Implémentée | `ApsCtpService.PromiseAsync`, journal `PromesseCapacitaireEmise` | `CtpPhase8Tests.Promise_flag_sets_reserves` | — |
| §13.2 #5 | Coût d'inertie δ dans fonction objectif | Partielle | `InertiaCostEngine`, `BarrierPolicyEngine` (`Phase9Engines.cs`) | `Phase9ContractsTests.Negotiable_applies_inertia_cost` | Modèle présent ; pas branché sur solveur MVP-1 |
| **Cahier §11.2 — Tests recette T0–T7** |||||
| T0 | Compilateur déterminisme — recompiler à froid → hash identique | Partielle | `ApsCompilerHash`, `ApsCompilerEngine` | `ApsCompilerEngineTests.Hash_is_deterministic`, `CompileSkeleton_same_sources_same_hash` | Pas de test bout-en-bout nommé T0 sur recompile SQL |
| T1 | Compilateur régénérabilité — jeter compilé, reconstruire, CBN identique | Partielle | `ApsCompilerService`, `ApsSegmentCbnService` | `ApsSqlIntegrationTests.Compiler_invalidates_and_rebuilds` | Invariant I6 partiellement couvert |
| T2 | Compilateur exactitude — V_besoins vs explosion naïve 20 articles | Absente | `ApsCompilerEngine` | — | Aucun benchmark 20 articles réels |
| T3 | CBN reproduit consommations réelles de fil | Partielle | `SegmentCbnEngine.cs` | `SegmentCbnAndYarnAtpTests.Yarn_atp_*`, `SegmentCbnAndYarnAtpTests.Netting_stops_at_decoupling_point_*` | Pas de replay historique fil réel |
| T4 | Charge calculée vs heures réellement passées | Partielle | `LoadEngine` | `FluxPhase7Tests.Machine_load_in_machine_hours`, `FluxPhase7Tests.Labour_load_in_person_hours` | Pas de corrélation données MES |
| T5 | Goulot identifié = vrai goulot | Partielle | `BottleneckEngine`, `ApsFluxService.cs` | `FluxPhase7Tests` (saturation), E2E flux | Validation terrain non automatisée |
| T6 | CTP déterminisme — deux exécutions → même date | Implémentée | `CtpEngine.cs` | `CtpPhase8Tests.Same_input_same_output` | — |
| T7 | Fiabilité promesse = taux de tenue réel | Absente | `CtpEngine` (champ fiabilité) | `CtpPhase8Tests.Reliability_to_confirm_without_estimators` | Estimateurs non calibrés sur historique |
| **Implémentations session récente** |||||
| Session | `Mvp0ApsGateGuard` — porte MVP-0 → APS REAL | Implémentée | `src/.../Application/Mvp0/Mvp0ApsGateGuard.cs`, injecté dans `ApsPlanningService`, `ApsCtpService` | `Mvp0ApsGateGuardTests.*` | Création garde ; DEMO bypass ; REAL exige GO |
| Session | Cascade CBN segments dans planification | Implémentée | `ApsPlanningService.cs`, `ApsCapacityCbnServices.RunCascadeAsync`, `SegmentCbnEngine.cs` | `SegmentCbnAndYarnAtpTests.Segment_netting_independent_*`, `NetCapacityAndSegmentCbnTests` | Option `RunSegmentCascade` sur `/aps/planification` |
| Session | `Mvp0ApsBridge` — promotion working set MVP-0 → APS | Implémentée | `Mvp0ApsBridgeService.cs`, `Mvp0ApsBridgeMapper.cs`, `SqlServerMvp0ApsBridgeRepository.cs` | `Mvp0ApsBridgeMapperTests.*` | Promotion campagne GO vers référentiel APS + journal |
| Session | Page `/consultation` — runs CBN, BOM, gammes | Implémentée | `src/.../Web/Components/Pages/Consultation.razor`, `ConsultationService` | `RoutesAuditTests` (`/consultation`), `NavigationReworkTests` | Page consultation référentiel et runs CBN |
| Session | Page `/aps/resultats` — historique planification | Implémentée | `src/.../Web/Components/Pages/ApsResultats.razor`, `ApsFluxService` | `RoutesAuditTests` (`/aps/resultats`), `ApsPagesTests` | Affichage runs publiés, ρ et goulot |
| Session | Gate CTP mode REAL (promesse capacitaire) | Implémentée | `ApsCtpService.cs` (`RequireApprovedCampaignAsync`) | `Mvp0ApsGateGuardTests.Real_source_requires_go` | CTP REAL bloqué sans campagne GO |
| **Transversal — pont MVP-0 / APS / consultation legacy** |||||
| — | Import BOM Excel/CSV (hors MVP-0) | Implémentée | `ImportService.cs`, `BomImportEngine`, `/imports/bom` | `BomImportEngineTests`, `ExcelImportAnalysisTests` | — |
| — | Simulation MRP pantalon (CBN legacy) | Implémentée | `Simulation.razor`, `SimulationCbnEngine` | `SimulationCbnEngineTests` | — |
| — | Pegging consultation | Implémentée | `PeggingService.cs`, `/pegging` | `DomainEngineTests.PeggingEngineTests` | — |
| — | Documentation routes APS complètes | Implémentée | `RoutesAuditTests.AllRoutes` (40+ routes) | `RoutesAuditTests`, `ApsPagesTests`, `LegacyPagesTests` | Routes `/consultation`, `/aps/resultats` ajoutées à l'audit |
| — | Authentification / multi-utilisateur | Absente | — | — | Hors périmètre v1 |
| — | Intégration ERP/MES temps réel | Absente | — | — | Hors périmètre v1 |
| — | Multi-site avec réallocation dynamique | Absente | — | — | Hors périmètre v1 |

---

## Synthèse

| Statut | Nombre de lignes |
|--------|-----------------|
| Implémentée | 32 |
| Partielle | 38 |
| Absente | 10 |
| Hors périmètre | 14 |
| **Total** | **94** |
