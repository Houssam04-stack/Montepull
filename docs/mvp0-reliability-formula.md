# MVP-0 — Formules de fiabilité

## Intrant (`Mvp0DataReliabilityEngine`)

Par domaine : `score = clamp(100 - BLOCK*25 - WARN*5 - BYPASS*8, 0, 100)`.

Global : moyenne pondérée SQL (`mvp0_reliability_weights`) − somme des pénalités by-pass.

Bandes : `GO_CANDIDATE` / `GO_WITH_RESERVATIONS_CANDIDATE` / `NO_GO_CANDIDATE` (seuils dans `mvp0_thresholds`, TO_CONFIRM).

## Résultat (`Mvp0BacktestReliabilityEngine.Score`)

Moyenne de scores date / durée / quantité / rebut / couverture / taux dans tolérance.

## Séparation

Intrant = complétude/cohérence. Résultat = adéquation standards vs réel.
