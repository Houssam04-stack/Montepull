# Guide utilisateur — MVP Axioplan Montepull

## Démarrage

1. Initialiser la base : `python scripts/init_db.py`
2. Lancer l'app : `dotnet run --project src/Axioplan.GammesNomenclatures.Web`
3. Ouvrir http://localhost:5280

## Pages

| Page | URL | Usage |
|---|---|---|
| Simulation | `/` | Générer variantes taille×couleur (tags SQL) |
| Imports | `/imports` | Importer commandes et nomenclatures Excel avec mapping modifiable |
| Paramètres | `/parametres` | Coefficients, pertes, règles CBN/Pegging |
| CBN | `/cbn` | Calcul besoins nets/bruts depuis commande |
| Pegging | `/pegging` | Lier besoins à OV / OF / OA / stock |
| Consultation | `/consultation` | Gammes, BOM, runs CBN, nomenclature aplatie |
| Articles | `/articles` | Configurateur article |

## Scénario DOUBLYGILF

1. `/imports` → importer `DOUBLYGILF.xlsx` dans la famille cible
2. `/imports` → importer `Nomenclature DOULBYGILF.xls` dans la même famille
3. `/cbn` → sélectionner la commande importée et la famille → **Calculer**
4. `/consultation` → vérifier la nomenclature de base / aplatie

## Notes Imports V1

- Les warnings d'import n'empêchent pas l'import tant qu'un minimum de données exploitables existe.
- Les tailles/couleurs manquantes sont créées automatiquement dans SQL Server avec coefficients par défaut à `1.0`.
- La nomenclature importée crée une nouvelle version BOM pour la famille sélectionnée.
- Si une colonne métier n'est pas reconnue, le mapping peut être corrigé manuellement avant validation.

## Notes Pegging V1

- Le pegging se lance a partir d'un run CBN existant.
- La page affiche trois niveaux de lecture : couverture des besoins, disponibilites utilisees, historique des liens.
- L'ordre de couverture V1 est : `stock` puis `OF` puis `OA`.
- Les quantites encore non couvertes sont visibles dans la colonne `Reste a couvrir`.
- Les politiques avancees d'allocation et les modes MTS/MTO restent `A confirmer`.

## Données simulées

Les composants FIL-MINT, PANNEAU-SF, VCOMP-STD, SACHET-STD et les commandes OA/OF sont **simulés** pour le MVP local. Ne pas les présenter comme données Sage confirmées.
