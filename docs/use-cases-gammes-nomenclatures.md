# Use Cases - Gammes & Nomenclatures

## Objectif

Décrire les cas d'utilisation et workflows du module Gammes & Nomenclatures.

## Confirmé

- Le module doit gérer familles, BOM de base, gammes de base, profils, simulation, génération, vues aplaties et traçabilité.
- Seuls les profils validés peuvent créer des données utilisables par le CBN.
- Les vues aplaties accélèrent le CBN et le calcul charge/capacité.

## Simulé

- Données locales article et composants.
- Coefficients MVP.
- BOM exemple.

## À confirmer

- Acteurs réels dans Axioplan.
- Droits utilisateurs.
- Workflow exact de validation métier.
- Politique CBN exacte.

## Acteurs

### Utilisateur métier

Rôle supposé :

- configure les familles ;
- prépare BOM et gammes de base ;
- lance les simulations ;
- vérifie les alertes ;
- demande ou effectue les validations.

À confirmer : intitulé réel du rôle.

### Système Axioplan

Rôle futur :

- fournit les référentiels ;
- consomme les données validées ;
- utilise les vues aplaties pour CBN/charge.

### Sage

Rôle futur :

- source ou destination de certaines données.

Hors MVP actuel.

## UC01 - Créer une famille produit fini

Objectif :

- déclarer une famille utilisée comme base de génération.

Entrées :

- code famille ;
- libellé ;
- type produit.

Sortie :

- ProductFamily créée.

Règles :

- le code doit être unique ;
- la famille pourra recevoir une BOM de base, une gamme de base et des profils.

## UC02 - Créer une BOM de base

Objectif :

- créer la nomenclature de référence d'une famille.

Entrées :

- famille produit fini ;
- code BOM ;
- version ;
- lignes composants.

Workflow :

1. Choisir la famille.
2. Créer l'en-tête BOM.
3. Ajouter les lignes.
4. Définir quantité, unité, perte et comportement.
5. Enregistrer en brouillon.

Sortie :

- BomBase avec BomBaseLine.

## UC03 - Créer une gamme de base

Objectif :

- créer la gamme de référence d'une famille.

Entrées :

- famille produit fini ;
- code gamme ;
- version ;
- opérations.

Workflow :

1. Choisir la famille.
2. Créer l'en-tête gamme.
3. Ajouter les opérations.
4. Référencer les workcenters.
5. Renseigner quantité et temps.
6. Enregistrer en brouillon.

Sortie :

- RoutingBase avec RoutingBaseOperation.

## UC04 - Créer un profil de génération

Objectif :

- définir comment le système peut générer des variantes, BOM et gammes.

Entrées :

- famille ;
- dimensions génératrices ;
- capacité à générer BOM ;
- capacité à générer gamme ;
- règles ;
- statut.

Workflow :

1. Choisir la famille.
2. Créer le profil.
3. Ajouter les dimensions.
4. Associer les règles.
5. Enregistrer en brouillon.
6. Passer à valider ou validé selon workflow métier.

Sortie :

- GenerationProfile.

## UC05 - Définir les coefficients de consommation

Objectif :

- définir les coefficients qui modifient les besoins composants.

Entrées :

- famille ;
- attribut ;
- valeur d'attribut ;
- coefficient.

Sortie :

- coefficient disponible pour les calculs de BOM.

À confirmer :

- source réelle des coefficients.

## UC06 - Définir les coefficients de temps

Objectif :

- définir les coefficients qui modifient les temps d'opération.

Entrées :

- famille ;
- attribut ;
- valeur d'attribut ;
- coefficient.

Sortie :

- coefficient disponible pour les calculs de gamme.

À confirmer :

- source réelle des coefficients.

## UC07 - Simuler une génération

Objectif :

- prévisualiser les variantes et impacts sans créer d'objets définitifs.

Entrées :

- profil ;
- valeurs de dimensions.

Workflow :

1. Vérifier le profil.
2. Vérifier les dimensions.
3. Construire les combinaisons demandées.
4. Appliquer les règles en mode simulation.
5. Produire résultats temporaires.
6. Produire alertes.

Sortie :

- GenerationSession en mode Simulation ;
- variantes simulées ;
- alertes ;
- prévisualisation BOM/gamme.

Règle :

- aucun objet définitif exploitable CBN n'est créé.

## UC08 - Générer les variantes

Objectif :

- créer les variantes demandées.

Entrées :

- profil validé ;
- valeurs de dimensions ;
- résultat de simulation éventuellement validé.

Workflow :

1. Vérifier que le profil est validé.
2. Vérifier les combinaisons demandées.
3. Créer une session de génération.
4. Créer les GeneratedVariant.
5. Générer BOM si le profil le permet.
6. Générer gamme si le profil le permet.
7. Tracer les calculs.
8. Produire les alertes.

Sortie :

- GenerationSession ;
- GeneratedVariant ;
- BomGeneratedVersion ;
- RoutingGeneratedVersion.

## UC09 - Générer une BOM dérivée

Objectif :

- produire une BOM pour une variante.

Workflow :

1. Charger la BOM de base.
2. Pour chaque ligne, appliquer son comportement.
3. Calculer besoin net si applicable.
4. Calculer besoin brut si perte.
5. Gérer les règles absentes selon stratégie.
6. Créer les lignes générées.
7. Tracer chaque calcul.

Sortie :

- BomGeneratedVersion ;
- BomGeneratedLine ;
- CalculationTrace.

## UC10 - Générer une gamme dérivée

Objectif :

- produire une gamme pour une variante.

Workflow :

1. Charger la gamme de base.
2. Pour chaque opération, appliquer son comportement.
3. Calculer le temps si applicable.
4. Gérer les règles absentes selon stratégie.
5. Créer les opérations générées.
6. Tracer chaque calcul.

Sortie :

- RoutingGeneratedVersion ;
- RoutingGeneratedOperation ;
- CalculationTrace.

## UC11 - Proposer des composants automatiquement

Objectif :

- suggérer des composants selon les attributs générés.

Exemple confirmé :

- vignette de composition par combinaison taille x couleur.

Workflow :

1. Lire les attributs de la variante.
2. Identifier les règles de proposition.
3. Créer les ProposedComponent.
4. Laisser l'utilisateur accepter, remplacer, refuser ou valider.

Sortie :

- composants proposés avec statut.

## UC12 - Gérer les cas sans règle automatique

Objectif :

- traiter les lignes non couvertes par une règle.

Stratégies confirmées :

- copier la valeur de base ;
- demander une saisie manuelle ;
- demander une sélection ;
- utiliser une valeur par défaut ;
- bloquer la génération ;
- marquer à compléter.

Sortie :

- ligne générée, alerte ou blocage selon stratégie.

## UC13 - Valider une BOM ou une gamme générée

Objectif :

- rendre l'objet utilisable selon la politique métier.

Workflow :

1. Vérifier le statut courant.
2. Vérifier les alertes.
3. Vérifier les lignes à compléter.
4. Appliquer la décision de validation.
5. Mettre à jour le statut.

À confirmer :

- qui valide ;
- quelles alertes bloquent ;
- politique exacte avant CBN.

## UC14 - Créer la nomenclature aplatie

Objectif :

- produire la projection matière destinée au CBN rapide.

Workflow :

1. Charger la BOM générée.
2. Parcourir les lignes.
3. Consolider par composant et unité.
4. Conserver les chemins sources.
5. Enregistrer le statut et la date de calcul.

Sortie :

- FlattenedBom.

## UC15 - Créer la gamme aplatie

Objectif :

- produire la projection charge destinée au calcul charge/capacité.

Workflow :

1. Charger la gamme générée.
2. Parcourir les opérations.
3. Consolider par workcenter ou workstation.
4. Conserver les chemins sources.
5. Enregistrer le statut et la date de calcul.

Sortie :

- FlattenedRouting.

## UC16 - Recalculer les vues aplaties

Objectif :

- mettre à jour les projections après modification d'une donnée structurante.

Déclencheurs confirmés :

- nouvelle version BOM ;
- modification coefficient taille/couleur ;
- modification conversion unité ;
- modification taux de perte ;
- modification gamme ou temps standard ;
- nouvelle commande client selon article spécifique commande.

Non déclencheur confirmé :

- modification stock.

## UC17 - Tracer les calculs

Objectif :

- permettre l'explication de chaque résultat.

Workflow :

1. Identifier la source.
2. Identifier la variante.
3. Identifier la règle.
4. Stocker valeur de base.
5. Stocker coefficients.
6. Stocker perte.
7. Stocker résultat.
8. Stocker origine manuelle si applicable.

Sortie :

- CalculationTrace.

## UC18 - Consulter les alertes de génération

Objectif :

- permettre le contrôle métier avant validation.

Types d'alertes :

- information ;
- avertissement ;
- erreur.

À confirmer :

- niveaux bloquants exacts.

## Hors use cases MVP actuel

- Créer un article via configurateur complet.
- Dupliquer un article.
- Importer des articles.
- Connexion Sage.
- Connexion Axioplan.
- IA planning.
- Interface graphique.

