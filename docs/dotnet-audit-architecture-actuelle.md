# Audit de l'architecture actuelle avant passage .NET

## Objectif

Analyser l'existant sans modifier le code applicatif, afin de préparer une architecture professionnelle intégrable dans Axioplan, application .NET.

## Confirmé

- Les documents métier restent la source de vérité.
- Le module prioritaire est le Configurateur Gammes & Nomenclatures.
- Le configurateur article complet est hors MVP actuel.
- L'IA de planification est hors MVP actuel.
- Les connexions Sage et Axioplan réelles sont hors MVP actuel.
- Sage, Axioplan réel et la base SQL réelle ne sont pas accessibles pour l'instant.
- Le MVP doit utiliser une base SQL locale simulée avec des données réalistes.
- Le fichier `prod` confirme uniquement les données visibles : opérations, workcenters, quantités et temps affichés.

## Simulé

- Les composants `FIL-MINT`, `VCOMP-STD` et `SACHET-STD`.
- La BOM exemple.
- Les coefficients taille/couleur utilisés dans les tests.
- Les libellés génériques des workcenters.
- Le profil `PROFILE_SIZE_COLOR` comme profil de démonstration.

## À confirmer

- Base SQL cible future.
- Type d'intégration Axioplan.
- Type d'intégration Sage.
- Conventions techniques .NET utilisées par Axioplan.
- Unité exacte des temps dans `prod`.
- Signification métier de `Besoin alloué`.
- Signification de `PE260268 / AH25PLUMIERE M MINT`.

## Ce qui peut être conservé

### Documentation métier et cadrage

À conserver.

Les fichiers suivants forment une bonne base de référence :

- `docs/Configurateur_Gammes_Nomenclatures (1).md`
- `docs/Cadrage_fonctionnel_configurateur_article (1).md`
- `docs/prod (1).md`
- `docs/architecture-mvp.md`
- `docs/modele-donnees.md`
- `docs/flux-generation.md`
- `docs/a-confirmer.md`

### Distinction Confirmé / Simulé / À confirmer

À conserver et renforcer.

Cette distinction est indispensable pour éviter de transformer une hypothèse de stage en règle métier réelle.

### Modèle SQL conceptuel

À conserver comme base d'inspiration.

Le schéma actuel couvre déjà les grandes zones :

- référentiel article minimal ;
- familles produit fini ;
- BOM de base ;
- gammes de base ;
- profils ;
- coefficients ;
- règles ;
- génération ;
- vues aplaties ;
- traces.

### Données `prod`

À conserver comme jeu de données confirmé partiel.

Le fichier `prod` est utile pour construire une gamme textile réaliste. Il ne confirme pas une BOM complète.

### Tests métier existants

À conserver comme premiers tests d'acceptation technique.

Ils devront être réécrits en tests .NET plus tard, mais les intentions restent valables :

- initialisation base ;
- chargement seeds ;
- 30 opérations chargées ;
- profil validé seulement utilisable ;
- calcul besoin net/brut ;
- calcul temps ;
- trace de calcul ;
- vue aplatie.

## Ce qui doit être adapté pour .NET

### Structure de projet

Le répertoire Python `src/axioplan` ne doit pas devenir le coeur final du projet.

Il faut passer vers une structure .NET simple :

```text
src/
  Axioplan.GammesNomenclatures.Domain/
  Axioplan.GammesNomenclatures.Application/
  Axioplan.GammesNomenclatures.Infrastructure/
  Axioplan.GammesNomenclatures.Api/
tests/
  Axioplan.GammesNomenclatures.Tests/
database/
docs/
```

### Python

Python devient uniquement transitoire.

À terme, la logique métier doit être portée en C#.

### SQLite

SQLite reste acceptable pour le développement local et les tests.

À terme, l'Infrastructure doit permettre de remplacer SQLite par la base utilisée par Axioplan, sans changer le Domain Model.

### SQL actuel

Le SQL actuel doit rester un support local de simulation.

Il ne doit pas dicter le Domain Model. Le métier doit rester au centre.

### Tests

Les tests Python doivent être remplacés par des tests .NET :

- tests unitaires du Domain ;
- tests Application sur les use cases ;
- tests Infrastructure sur les repositories locaux ;
- tests d'intégration avec base locale simulée.

## Risques actuels

- Confondre données simulées et données confirmées.
- Construire le moteur autour du SQL au lieu du métier.
- Développer une interface trop tôt.
- Développer le configurateur article complet trop tôt.
- Préparer l'IA planning avant d'avoir des BOM/gammes fiables.
- Trop abstraire les connecteurs Sage/Axioplan avant de connaître leurs contrats.

## Décision d'orientation

Le projet doit évoluer vers une architecture .NET modulaire et intégrable à Axioplan.

Le module Gammes & Nomenclatures doit être conçu comme un module métier autonome, avec :

- Domain Model indépendant ;
- use cases explicites ;
- Infrastructure remplaçable ;
- base locale simulée ;
- future couche d'intégration Sage/Axioplan non développée dans le MVP actuel.

