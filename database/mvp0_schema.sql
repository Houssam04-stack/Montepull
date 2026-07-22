-- MVP-0 schema (additif) — aussi créé via Mvp0SchemaBootstrap au démarrage.
-- Ne pas supprimer les tables APS / référentiel existantes.
USE AxioplanMvp;
GO

-- Tables principales : mvp0_campaigns, mvp0_import_batches, mvp0_import_files,
-- mvp0_import_mappings, mvp0_working_sets, mvp0_validation_rules, mvp0_validation_runs,
-- mvp0_anomalies, mvp0_bypasses, mvp0_bypass_history, mvp0_reliability_weights,
-- mvp0_thresholds, mvp0_scores, mvp0_reports
--
-- Source de vérité runtime : Infrastructure/Mvp0/SqlServerMvp0Repository.cs (Ddl)
-- Seuils & poids seedés avec note TO_CONFIRM.
