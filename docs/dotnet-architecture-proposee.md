# Architecture .NET proposée

## Objectif

Proposer une architecture .NET simple, professionnelle et adaptée à un stage d'un mois, pour préparer l'intégration future du module Gammes & Nomenclatures dans Axioplan.

## Confirmé

- Axioplan est une application .NET.
- Le module doit pouvoir être intégré plus tard dans Axioplan.
- Sage, Axioplan réel et la base SQL réelle ne sont pas accessibles pour l'instant.
- La base locale reste simulée mais réaliste.
- Les connecteurs Sage/Axioplan sont hors MVP actuel.
- L'interface graphique est hors MVP actuel.

## Simulé

- Base locale de développement.
- Données de référence nécessaires au MVP.
- Composants et coefficients non confirmés.

## À confirmer

- Version .NET cible.
- ORM ou accès SQL utilisé par Axioplan.
- Style d'API interne attendu par Axioplan.
- Base SQL cible.
- Conventions de nommage Axioplan.
- Stratégie d'authentification et droits.

## Architecture cible simple

```text
src/
  Axioplan.GammesNomenclatures.Domain/
  Axioplan.GammesNomenclatures.Application/
  Axioplan.GammesNomenclatures.Infrastructure/
  Axioplan.GammesNomenclatures.Api/

tests/
  Axioplan.GammesNomenclatures.Domain.Tests/
  Axioplan.GammesNomenclatures.Application.Tests/
  Axioplan.GammesNomenclatures.Infrastructure.Tests/

database/
  schema/
  seed/

docs/
```

## Responsabilités des couches

### Domain

Contient le métier pur.

Responsabilités :

- entités métier ;
- value objects ;
- statuts ;
- règles invariantes ;
- calculs métier purs ;
- comportements de génération indépendants de la base.

Ne contient pas :

- SQL ;
- Entity Framework ;
- API ;
- fichiers ;
- dépendance Sage/Axioplan.

### Application

Orchestre les cas d'utilisation.

Responsabilités :

- créer une famille ;
- créer une BOM de base ;
- créer une gamme de base ;
- créer un profil ;
- simuler une génération ;
- générer BOM/gammes ;
- valider ;
- créer les vues aplaties ;
- tracer les calculs.

Contient :

- commandes ;
- handlers ;
- interfaces de repositories ;
- DTO internes si nécessaires ;
- ports vers Infrastructure.

### Infrastructure

Gère les détails techniques.

Responsabilités MVP :

- base SQL locale simulée ;
- repositories ;
- chargement des seeds ;
- transactions ;
- persistance des traces ;
- persistance des vues aplaties.

Responsabilités futures :

- adaptateur Sage ;
- adaptateur Axioplan ;
- remplacement de la base simulée par la vraie base.

### API

Exposition technique minimale, sans interface graphique.

Responsabilités possibles :

- endpoints internes de test ;
- endpoints d'orchestration des use cases ;
- health check ;
- endpoints de simulation.

À confirmer : l'API peut être reportée si Axioplan attend une intégration directe par service interne.

### Database

Support local de développement.

Responsabilités :

- scripts de création ;
- scripts de données simulées ;
- jeux de données `prod` confirmés partiels ;
- migrations locales si retenues plus tard.

## Dépendances autorisées

```text
Api -> Application -> Domain
Infrastructure -> Application
Infrastructure -> Domain
Tests -> toutes les couches nécessaires
```

Le Domain ne dépend de rien.

## Connecteurs futurs

Les connecteurs ne sont pas développés maintenant.

Prévoir seulement des interfaces :

- `IArticleReferenceProvider`
- `IWorkcenterReferenceProvider`
- `IRoutingReferenceProvider`
- `IBomReferenceProvider`
- `IAxioplanIntegrationPort`
- `ISageIntegrationPort`

Ces noms sont des propositions techniques, pas des règles métier.

## Choix MVP recommandé

Pour un stage d'un mois :

1. Construire Domain + Application.
2. Garder Infrastructure locale simple.
3. Ajouter une API minimale seulement si utile pour tester.
4. Ne pas créer d'interface graphique.
5. Ne pas développer Sage/Axioplan.
6. Ne pas développer l'IA planning.

## Pourquoi cette architecture est adaptée

- Simple à comprendre.
- Testable.
- Compatible avec une future intégration .NET.
- Le SQL local peut être remplacé.
- Le métier n'est pas enfermé dans la base.
- Le module reste indépendant.

