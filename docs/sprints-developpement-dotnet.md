# Découpage en sprints - Développement .NET

## Objectif

Préparer le développement du module Gammes & Nomenclatures en tâches courtes, réalisables dans Cursor, sans développer encore l'interface, le configurateur article complet, l'IA planning ou les connecteurs Sage/Axioplan.

## Hypothèses de planning

Statut : Supposé.

- Stage d'un mois.
- Tâches de 2 à 4 heures.
- Priorité à un coeur métier testable.
- API minimale seulement si utile.
- Base locale simulée conservée.

## Sprint 0 - Préparation .NET et cadrage technique

Objectif :

- créer le squelette .NET sans logique métier avancée.

### Tâche 0.1 - Créer la solution .NET

Durée : 2 h.

Livrable :

- solution `.sln` ;
- projets Domain, Application, Infrastructure, Api, Tests.

### Tâche 0.2 - Configurer les références entre projets

Durée : 2 h.

Livrable :

- dépendances conformes à l'architecture.

### Tâche 0.3 - Mettre en place le socle de tests

Durée : 2 h.

Livrable :

- projet de tests ;
- premier test vide ou test de compilation.

### Tâche 0.4 - Ajouter les documents comme références projet

Durée : 2 h.

Livrable :

- liens vers documents métier et architecture.

## Sprint 1 - Domain Model

Objectif :

- construire les objets métier sans persistance.

### Tâche 1.1 - Créer les statuts métier

Durée : 2 h.

Livrable :

- statuts de profil ;
- statuts d'objet généré ;
- statuts de composant proposé.

### Tâche 1.2 - Créer ProductFamily, BomBase, BomBaseLine

Durée : 3 h.

Livrable :

- entités de base BOM ;
- tests de création simples.

### Tâche 1.3 - Créer RoutingBase, RoutingBaseOperation

Durée : 3 h.

Livrable :

- entités de base gamme ;
- tests de séquence opérations.

### Tâche 1.4 - Créer GenerationProfile et GenerationDimension

Durée : 3 h.

Livrable :

- profil avec dimensions ;
- règle profil validé.

### Tâche 1.5 - Créer GenerationSession et GeneratedVariant

Durée : 3 h.

Livrable :

- session simulation/génération ;
- variantes générables.

## Sprint 2 - Règles de calcul métier

Objectif :

- implémenter les règles pures du moteur.

### Tâche 2.1 - Calcul besoin net/brut

Durée : 2 h.

Livrable :

- calcul besoin net ;
- calcul besoin brut ;
- tests.

### Tâche 2.2 - Calcul temps opération

Durée : 2 h.

Livrable :

- calcul temps par coefficients ;
- tests.

### Tâche 2.3 - Gestion des comportements de ligne BOM

Durée : 4 h.

Livrable :

- fixe ;
- calculée ;
- copiée à valider ;
- stratégie pour les autres comportements.

### Tâche 2.4 - Gestion des comportements d'opération gamme

Durée : 4 h.

Livrable :

- copie ;
- calcul ;
- à valider ;
- tests.

### Tâche 2.5 - Gestion des cas sans règle

Durée : 4 h.

Livrable :

- copier ;
- saisie manuelle ;
- sélection ;
- défaut ;
- blocage ;
- à compléter.

## Sprint 3 - Use Cases Application

Objectif :

- orchestrer le Domain Model.

### Tâche 3.1 - Créer famille produit fini

Durée : 2 h.

Livrable :

- use case ;
- tests Application.

### Tâche 3.2 - Créer BOM de base

Durée : 3 h.

Livrable :

- use case ;
- validation minimale.

### Tâche 3.3 - Créer gamme de base

Durée : 3 h.

Livrable :

- use case ;
- import possible des opérations simulées.

### Tâche 3.4 - Créer profil de génération

Durée : 3 h.

Livrable :

- use case ;
- dimensions ;
- statut.

### Tâche 3.5 - Simuler génération Taille x Couleur

Durée : 4 h.

Livrable :

- session simulation ;
- liste variantes ;
- aucune donnée définitive.

### Tâche 3.6 - Générer variantes Taille x Couleur

Durée : 4 h.

Livrable :

- session génération ;
- variantes ;
- contrôle profil validé.

## Sprint 4 - BOM, gamme générées et traçabilité

Objectif :

- générer les objets dérivés et les traces.

### Tâche 4.1 - Générer BOM dérivée

Durée : 4 h.

Livrable :

- BomGeneratedVersion ;
- BomGeneratedLine ;
- tests.

### Tâche 4.2 - Générer gamme dérivée

Durée : 4 h.

Livrable :

- RoutingGeneratedVersion ;
- RoutingGeneratedOperation ;
- tests.

### Tâche 4.3 - Créer CalculationTrace

Durée : 3 h.

Livrable :

- trace complète ;
- tests d'explication de calcul.

### Tâche 4.4 - Créer GenerationAlert

Durée : 3 h.

Livrable :

- alertes info/warning/error ;
- tests cas sans règle.

## Sprint 5 - Vues aplaties

Objectif :

- préparer CBN et charge sans implémenter le CBN complet.

### Tâche 5.1 - Calculer nomenclature aplatie

Durée : 4 h.

Livrable :

- consolidation composants ;
- chemins sources ;
- statut.

### Tâche 5.2 - Calculer gamme aplatie

Durée : 4 h.

Livrable :

- consolidation temps par workcenter ;
- chemins sources ;
- statut.

### Tâche 5.3 - Gérer déclencheurs de recalcul

Durée : 3 h.

Livrable :

- liste de causes ;
- tests de décision recalcul.

## Sprint 6 - Infrastructure locale

Objectif :

- connecter le Domain/Application à une base locale simulée.

### Tâche 6.1 - Choisir persistance locale

Durée : 2 h.

Livrable :

- décision documentée : SQLite, LocalDB ou autre.

À confirmer selon environnement Axioplan.

### Tâche 6.2 - Créer repositories locaux

Durée : 4 h.

Livrable :

- repositories familles, BOM, gammes, profils.

### Tâche 6.3 - Charger seeds simulés

Durée : 3 h.

Livrable :

- données `prod` pour opérations ;
- données simulées clairement identifiées.

### Tâche 6.4 - Tests d'intégration base locale

Durée : 4 h.

Livrable :

- chargement base ;
- génération ;
- vues aplaties ;
- traces.

## Sprint 7 - API minimale optionnelle

Objectif :

- exposer les use cases si nécessaire.

Statut : À confirmer.

### Tâche 7.1 - Endpoint simulation

Durée : 3 h.

Livrable :

- POST simulation.

### Tâche 7.2 - Endpoint génération

Durée : 3 h.

Livrable :

- POST génération.

### Tâche 7.3 - Endpoint consultation résultats

Durée : 3 h.

Livrable :

- GET session ;
- GET alertes ;
- GET traces.

## Sprint 8 - Stabilisation et démonstration

Objectif :

- rendre le MVP présentable.

### Tâche 8.1 - Nettoyer documentation technique

Durée : 2 h.

Livrable :

- documentation à jour.

### Tâche 8.2 - Vérifier Confirmé / Simulé / À confirmer

Durée : 2 h.

Livrable :

- revue des données et règles.

### Tâche 8.3 - Préparer scénario de démonstration

Durée : 3 h.

Livrable :

- famille pull ;
- gamme `prod` ;
- BOM simulée ;
- génération Taille x Couleur ;
- vues aplaties ;
- traces.

### Tâche 8.4 - Revue finale des tests

Durée : 3 h.

Livrable :

- tests verts ;
- risques connus documentés.

## Hors planning actuel

- Interface graphique.
- Configurateur article complet.
- IA planning.
- Connexion Sage.
- Connexion Axioplan.
- CBN complet.

