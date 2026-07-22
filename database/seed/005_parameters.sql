INSERT INTO mvp_parameter_groups (code, label, position) VALUES
('COEFFICIENTS', 'Coefficients de generation', 10),
('LOSSES', 'Taux de perte', 20),
('CBN', 'Regles CBN', 30);

INSERT INTO mvp_parameters (group_id, code, label, value_type, scope, description)
SELECT id, 'CBN_REQUIRE_VALIDATED_PROFILE', 'Profil valide requis pour generation CBN',
       'BOOLEAN', 'GLOBAL', 'Si vrai, seuls les profils VALIDATED declenchent une generation exploitable.'
FROM mvp_parameter_groups WHERE code = 'CBN';

INSERT INTO mvp_parameters (group_id, code, label, value_type, scope, description)
SELECT id, 'CBN_ACCEPTED_BOM_STATUSES', 'Statuts BOM acceptes pour calcul CBN',
       'TEXT', 'GLOBAL', 'Liste separee par virgule. A confirmer avec le metier.'
FROM mvp_parameter_groups WHERE code = 'CBN';

INSERT INTO mvp_parameters (group_id, code, label, value_type, scope, description)
SELECT id, 'CBN_LOSS_FORMULA', 'Formule besoin brut',
       'TEXT', 'GLOBAL', 'Formule documentee : net / (1 - loss_rate). Lecture seule metier.'
FROM mvp_parameter_groups WHERE code = 'CBN';

INSERT INTO mvp_parameter_values (parameter_id, scope_key, value_text)
SELECT p.id, NULL, 'true'
FROM mvp_parameters p WHERE p.code = 'CBN_REQUIRE_VALIDATED_PROFILE';

INSERT INTO mvp_parameter_values (parameter_id, scope_key, value_text)
SELECT p.id, NULL, 'DRAFT,VALIDATED,GENERATED'
FROM mvp_parameters p WHERE p.code = 'CBN_ACCEPTED_BOM_STATUSES';

INSERT INTO mvp_parameter_values (parameter_id, scope_key, value_text)
SELECT p.id, NULL, 'net / (1 - loss_rate)'
FROM mvp_parameters p WHERE p.code = 'CBN_LOSS_FORMULA';

INSERT INTO mvp_parameter_groups (code, label, position) VALUES
('PEGGING', 'Regles Pegging', 40);

INSERT INTO mvp_parameters (group_id, code, label, value_type, scope, description)
SELECT id, 'PEGGING_MAX_CASCADE_LEVEL', 'Profondeur max cascade BOM',
       'NUMBER', 'GLOBAL', 'Nombre max de niveaux BOM pour explosion. A confirmer.'
FROM mvp_parameter_groups WHERE code = 'PEGGING';

INSERT INTO mvp_parameters (group_id, code, label, value_type, scope, description)
SELECT id, 'PEGGING_BIDIRECTIONAL_LINKS', 'Creer liens bidirectionnels',
       'BOOLEAN', 'GLOBAL', 'Si vrai, chaque lien DOWNSTREAM genere un lien UPSTREAM miroir.'
FROM mvp_parameter_groups WHERE code = 'PEGGING';

INSERT INTO mvp_parameters (group_id, code, label, value_type, scope, description)
SELECT id, 'CBN_AUTO_RECALC_ON_PARAM_CHANGE', 'Recalcul CBN apres changement parametre',
       'BOOLEAN', 'GLOBAL', 'Si vrai, un changement de parametre marque les runs pour recalcul.'
FROM mvp_parameter_groups WHERE code = 'CBN';

INSERT INTO mvp_parameter_values (parameter_id, scope_key, value_text)
SELECT p.id, NULL, '5'
FROM mvp_parameters p WHERE p.code = 'PEGGING_MAX_CASCADE_LEVEL';

INSERT INTO mvp_parameter_values (parameter_id, scope_key, value_text)
SELECT p.id, NULL, 'true'
FROM mvp_parameters p WHERE p.code = 'PEGGING_BIDIRECTIONAL_LINKS';

INSERT INTO mvp_parameter_values (parameter_id, scope_key, value_text)
SELECT p.id, NULL, 'true'
FROM mvp_parameters p WHERE p.code = 'CBN_AUTO_RECALC_ON_PARAM_CHANGE';
