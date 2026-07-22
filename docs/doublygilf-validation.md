# Validation DOUBLYGILF — ecarts documentes

## Contexte

Les fichiers source DOUBLYGILF n'ont pas ete fournis dans le depot. Le jeu de test
`database/seed/004_doublygilf.sql` est derive de `docs/prod (1).md` :

| Champ | Valeur |
|---|---|
| Commande | `DOUBLYGILF-OV-001` |
| Ref. externe | `PE260268 / AH25PLUMIERE M MINT` |
| Article | `AH25PLUMIERE` |
| Taille / couleur | M / MINT |
| Quantite | 30 pieces |

Nomenclature utilisee : `BOM_BASE_PULL_COL_ROND_EXEMPLE` (donnees simulees MVP).

## Resultats CBN attendus (MVP)

Pour la ligne DOUBLYGILF, famille `PULL_COL_ROND`, coefficients M=1,0 et MINT=1,0 :

| Composant | Net | Brut | Unite | Formule |
|---|---:|---:|---|---|
| FIL-MINT | 15,000 | 15,789 | KG | 0,5 x 1,0 x 1,0 x 30 / (1 - 0,05) |
| VCOMP-STD | 30,000 | 30,000 | PIECE | 1,0 x 30 |
| SACHET-STD | 30,000 | 30,000 | PIECE | 1,0 x 30 |

## Ecart avec le fichier prod

Le fichier prod (`docs/prod (1).md`) mentionne un **besoin alloue de 152,094**
pour la meme commande. Ce montant ne correspond pas au calcul MVP ci-dessus.

### Causes probables — a confirmer

1. **Nomenclature reelle** : le BOM prod n'est pas le BOM exemple MVP (FIL-MINT / VCOMP-STD / SACHET-STD).
2. **Unite du besoin alloue** : l'unite de 152,094 n'est pas documentee (fil ? temps ? autre ?).
3. **Coefficients** : coefficients taille/couleur ou pertes differents en production.
4. **Pegging / allocation** : le besoin alloue peut inclure d'autres lignes ou regles non modelisees.
5. **Multi-niveaux** : le MVP n'aplatit qu'un seul niveau de nomenclature.

## Parametres CBN utilises

Lus depuis `mvp_parameters` (groupe CBN) :

- `CBN_REQUIRE_VALIDATED_PROFILE` = true
- `CBN_ACCEPTED_BOM_STATUSES` = DRAFT,VALIDATED,GENERATED (DRAFT inclus pour tests locaux — a confirmer)
- `CBN_LOSS_FORMULA` = net / (1 - loss_rate)

## Verification

```bash
python scripts/init_db.py
python -m unittest tests.test_doublygilf -v
dotnet run --project src/Axioplan.GammesNomenclatures.Web
# Page /cbn : commande DOUBLYGILF-OV-001, famille Pull col rond
```
