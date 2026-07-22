# Roadmap officielle - Axioplan Gammes & Nomenclatures

## Objectif

Cette roadmap est la feuille de route officielle du développement du MVP Gammes & Nomenclatures pour Montepull.

Le module doit être préparé pour une intégration future dans Axioplan, application .NET, tout en restant réalisable dans le cadre d'un stage d'un mois.

## Périmètre confirmé

- Module concerné : Configurateur Gammes & Nomenclatures.
- Architecture cible : .NET.
- Base locale : SQL simulée mais réaliste.
- Données réelles non accessibles pour l'instant : Sage, Axioplan, base SQL réelle.
- Les documents métier restent la source de vérité.
- Le fichier `prod` confirme uniquement les opérations, workcenters, quantités et temps visibles.

## Données simulées

- Composants `FIL-MINT`, `VCOMP-STD`, `SACHET-STD`.
- BOM exemple.
- Coefficients taille/couleur de démonstration.
- Libellés génériques des workcenters.
- Base locale de développement.

## À confirmer

- Version .NET cible.
- ORM ou accès SQL utilisé par Axioplan.
- Base SQL cible.
- Conventions techniques Axioplan.
- Unité exacte des temps.
- Politique CBN exacte.
- Niveau d'intégration attendu avec Axioplan.

## Hors MVP actuel

- Connexion Sage réelle
- Connexion Axioplan réelle
- IA de planification
- Pegging complet (allocation métier avancée, capacité)

## Architecture cible

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

## Sprint 0 - Préparation .NET et cadrage technique

### Objectif

Créer le squelette .NET du projet sans développer encore le moteur métier.

### Fichiers concernés

- `Axioplan.GammesNomenclatures.sln`
- `src/Axioplan.GammesNomenclatures.Domain/`
- `src/Axioplan.GammesNomenclatures.Application/`
- `src/Axioplan.GammesNomenclatures.Infrastructure/`
- `src/Axioplan.GammesNomenclatures.Api/`
- `tests/Axioplan.GammesNomenclatures.Domain.Tests/`
- `tests/Axioplan.GammesNomenclatures.Application.Tests/`
- `tests/Axioplan.GammesNomenclatures.Infrastructure.Tests/`
- `docs/dotnet-architecture-proposee.md`

### Technologies utilisées

- .NET, version à confirmer.
- C#.
- xUnit ou NUnit, à confirmer.
- Base locale simulée conservée.

### Résultat attendu

- Solution .NET créée.
- Projets créés.
- Références entre projets conformes à l'architecture.
- Premier test de compilation.
- Documentation reliée au projet.

### Durée estimée

8 heures.

## Sprint 1 - Domain Model métier

### Objectif

Construire les objets métier purs, sans SQL, sans API et sans dépendance Infrastructure.

### Fichiers concernés

- `src/Axioplan.GammesNomenclatures.Domain/`
- `tests/Axioplan.GammesNomenclatures.Domain.Tests/`
- `docs/domain-model-gammes-nomenclatures.md`
- `docs/business-rules-gammes-nomenclatures.md`

### Technologies utilisées

- C#.
- Tests unitaires .NET.
- Domain Model sans Entity Framework.

### Résultat attendu

- Statuts métier créés.
- Entités principales créées :
  - `ProductFamily`
  - `BomBase`
  - `BomBaseLine`
  - `RoutingBase`
  - `RoutingBaseOperation`
  - `GenerationProfile`
  - `GenerationDimension`
  - `GenerationSession`
  - `GeneratedVariant`
- Tests unitaires des invariants de base.

### Durée estimée

14 heures.

## Sprint 2 - Règles de calcul métier

### Objectif

Implémenter les règles métier pures du moteur de génération.

### Fichiers concernés

- `src/Axioplan.GammesNomenclatures.Domain/`
- `tests/Axioplan.GammesNomenclatures.Domain.Tests/`
- `docs/business-rules-gammes-nomenclatures.md`

### Technologies utilisées

- C#.
- Tests unitaires .NET.

### Résultat attendu

- Calcul besoin net.
- Calcul besoin brut.
- Calcul temps opération.
- Gestion des comportements de ligne BOM :
  - fixe ;
  - calculée ;
  - copiée à valider ;
  - autres comportements préparés.
- Gestion des comportements d'opération gamme.
- Gestion des cas sans règle :
  - copie ;
  - saisie manuelle ;
  - sélection ;
  - valeur par défaut ;
  - blocage ;
  - à compléter.

### Durée estimée

16 heures.

## Sprint 3 - Use Cases Application

### Objectif

Créer la couche Application qui orchestre le Domain Model.

### Fichiers concernés

- `src/Axioplan.GammesNomenclatures.Application/`
- `src/Axioplan.GammesNomenclatures.Domain/`
- `tests/Axioplan.GammesNomenclatures.Application.Tests/`
- `docs/use-cases-gammes-nomenclatures.md`

### Technologies utilisées

- C#.
- Tests Application.
- Interfaces de repositories.
- Commandes et handlers simples.

### Résultat attendu

- Use case créer famille produit fini.
- Use case créer BOM de base.
- Use case créer gamme de base.
- Use case créer profil de génération.
- Use case simuler génération Taille x Couleur.
- Use case générer variantes Taille x Couleur.
- Contrôle : seuls les profils validés peuvent générer des objets exploitables.

### Durée estimée

19 heures.

## Sprint 4 - Génération BOM, gamme et traçabilité

### Objectif

Produire les objets dérivés et conserver l'explication des calculs.

### Fichiers concernés

- `src/Axioplan.GammesNomenclatures.Domain/`
- `src/Axioplan.GammesNomenclatures.Application/`
- `tests/Axioplan.GammesNomenclatures.Domain.Tests/`
- `tests/Axioplan.GammesNomenclatures.Application.Tests/`
- `docs/business-rules-gammes-nomenclatures.md`
- `docs/use-cases-gammes-nomenclatures.md`

### Technologies utilisées

- C#.
- Tests unitaires.
- Tests Application.

### Résultat attendu

- `BomGeneratedVersion`.
- `BomGeneratedLine`.
- `RoutingGeneratedVersion`.
- `RoutingGeneratedOperation`.
- `CalculationTrace`.
- `GenerationAlert`.
- Tests de génération et d'explication de calcul.

### Durée estimée

14 heures.

## Sprint 5 - Vues aplaties

### Objectif

Préparer le CBN matière et le calcul charge/capacité sans développer le CBN complet.

### Fichiers concernés

- `src/Axioplan.GammesNomenclatures.Domain/`
- `src/Axioplan.GammesNomenclatures.Application/`
- `tests/Axioplan.GammesNomenclatures.Domain.Tests/`
- `tests/Axioplan.GammesNomenclatures.Application.Tests/`
- `docs/flux-generation.md`
- `docs/business-rules-gammes-nomenclatures.md`

### Technologies utilisées

- C#.
- Tests unitaires.
- Tests Application.

### Résultat attendu

- Nomenclature aplatie.
- Gamme aplatie.
- Consolidation des composants.
- Consolidation des temps par workcenter.
- Chemins sources conservés.
- Statuts et métadonnées de calcul.
- Décision de recalcul selon les déclencheurs confirmés.

### Durée estimée

11 heures.

## Sprint 6 - Infrastructure locale

### Objectif

Brancher la couche Application sur une base locale simulée remplaçable plus tard.

### Fichiers concernés

- `src/Axioplan.GammesNomenclatures.Infrastructure/`
- `src/Axioplan.GammesNomenclatures.Application/`
- `database/schema.sql`
- `database/seed/001_referentials.sql`
- `database/seed/002_prod_example.sql`
- `tests/Axioplan.GammesNomenclatures.Infrastructure.Tests/`

### Technologies utilisées

- C#.
- SQL local.
- SQLite ou autre base locale, à confirmer.
- Entity Framework Core ou accès SQL direct, à confirmer.
- Tests d'intégration.

### Résultat attendu

- Choix de persistance locale documenté.
- Repositories locaux.
- Chargement des seeds.
- Données `prod` chargées pour les opérations.
- Données simulées clairement identifiées.
- Tests d'intégration :
  - base créée ;
  - seeds chargés ;
  - 30 opérations chargées ;
  - génération persistée ;
  - vues aplaties persistées ;
  - traces persistées.

### Durée estimée

13 heures.

## Sprint 7 - API minimale optionnelle

### Objectif

Exposer les use cases uniquement si cela aide l'intégration ou la démonstration.

Statut : À confirmer.

### Fichiers concernés

- `src/Axioplan.GammesNomenclatures.Api/`
- `src/Axioplan.GammesNomenclatures.Application/`
- `tests/Axioplan.GammesNomenclatures.Application.Tests/`
- éventuellement `tests/Axioplan.GammesNomenclatures.Api.Tests/`

### Technologies utilisées

- ASP.NET Core, si API confirmée.
- C#.
- Tests API, si retenus.

### Résultat attendu

- Endpoint simulation.
- Endpoint génération.
- Endpoint consultation session.
- Endpoint consultation alertes.
- Endpoint consultation traces.

Si l'intégration Axioplan attend des services internes plutôt qu'une API HTTP, ce sprint peut être remplacé par une couche de services applicatifs.

### Durée estimée

9 heures.

## Sprint 8 - Stabilisation et démonstration

### Objectif

Rendre le MVP cohérent, testable et présentable.

### Fichiers concernés

- `README.md`
- `Roadmap.md`
- `docs/`
- `src/`
- `tests/`
- `database/`

### Technologies utilisées

- .NET.
- C#.
- SQL local.
- Tests automatisés.
- Documentation Markdown.

### Résultat attendu

- Documentation alignée.
- Distinction Confirmé / Simulé / À confirmer vérifiée.
- Scénario de démonstration :
  - famille pull ;
  - gamme issue du fichier `prod` ;
  - BOM simulée ;
  - génération Taille x Couleur ;
  - BOM générée ;
  - gamme générée ;
  - vues aplaties ;
  - traces de calcul.
- Tests verts.
- Risques et limites connus documentés.

### Durée estimée

10 heures.

## Synthèse des durées

| Sprint | Durée estimée |
|---|---:|
| Sprint 0 - Préparation .NET | 8 h |
| Sprint 1 - Domain Model | 14 h |
| Sprint 2 - Règles métier | 16 h |
| Sprint 3 - Use Cases Application | 19 h |
| Sprint 4 - Génération et traçabilité | 14 h |
| Sprint 5 - Vues aplaties | 11 h |
| Sprint 6 - Infrastructure locale | 13 h |
| Sprint 7 - API optionnelle | 9 h |
| Sprint 8 - Stabilisation | 10 h |
| Total avec API | 114 h |
| Total sans API | 105 h |

## Priorité MVP recommandée

Pour tenir le stage d'un mois, l'ordre prioritaire est :

1. Sprint 0.
2. Sprint 1.
3. Sprint 2.
4. Sprint 3.
5. Sprint 4.
6. Sprint 5.
7. Sprint 6.
8. Sprint 8.

Le Sprint 7 API reste optionnel tant que le mode d'intégration Axioplan n'est pas confirmé.

## Critères de réussite du MVP

- Le Domain Model reflète les documents métier.
- Les règles métier sont testées.
- Une génération Taille x Couleur fonctionne.
- Les BOM et gammes générées sont traçables.
- Les vues aplaties sont calculées.
- Les données simulées sont clairement séparées des données confirmées.
- L'architecture reste intégrable à Axioplan.

