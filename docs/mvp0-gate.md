# MVP-0 — Gate

`Mvp0GateDecisionEngine` :

1. SIMULATED → `INSUFFICIENT_DATA`
2. Planificateur non validé → `INSUFFICIENT_DATA` (jamais décision auto seule)
3. BLOCKING non by-passé → `NO_GO`
4. Scores ≥ seuil GO et 0 by-pass → `GO`
5. Scores ≥ seuil GO_WITH_RESERVATIONS → `GO_WITH_RESERVATIONS`
6. Sinon → `NO_GO`

Seuils en SQL : `mvp0_thresholds`.
