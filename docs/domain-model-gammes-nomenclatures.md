# Domain Model - Gammes & Nomenclatures

## Objectif

Décrire les objets métier du module Gammes & Nomenclatures sans parler de SQL.

Ce document sert de base au futur Domain Model .NET.

## Confirmé

- Une nomenclature de base et une gamme de base sont définies par famille de produit fini.
- Les profils de génération sont multiples, paramétrables et validés.
- La génération peut être unitaire ou multiple.
- La combinatoire est maîtrisée : minimum Taille x Couleur, maximum Taille x Couleur x Commande x Composition.
- Les besoins et temps peuvent être calculés par coefficients.
- Les vues aplaties servent au CBN et au calcul de charge.
- La traçabilité des calculs est obligatoire.
- Workcenters, workstations et calendriers existent déjà dans Axioplan et sont seulement référencés.

## Simulé

- Les données article locales nécessaires au MVP.
- Les composants non fournis par `prod`.
- Les coefficients de calcul du MVP.

## À confirmer

- Liste officielle des familles.
- Liste officielle des attributs générateurs actifs par client.
- Unités réelles des temps.
- Codification article réelle.
- Politique CBN exacte par statut.

## Agrégats principaux

### ProductFamily

Famille de produit fini utilisée comme point d'entrée de génération.

Responsabilités :

- porter l'identité métier de la famille ;
- rattacher une BOM de base ;
- rattacher une gamme de base ;
- rattacher des profils de génération.

Relations :

- possède zéro ou une BOM de base active ;
- possède zéro ou une gamme de base active ;
- possède plusieurs profils de génération.

### BomBase

Nomenclature de base d'une famille produit fini.

Responsabilités :

- représenter la structure technique de référence ;
- porter une version ;
- contenir des lignes de composants ;
- servir de source aux BOM générées.

Relations :

- appartient à une ProductFamily ;
- contient plusieurs BomBaseLine ;
- source de plusieurs BomGeneratedVersion.

### BomBaseLine

Ligne de nomenclature de base.

Responsabilités :

- référencer un composant ;
- porter une quantité de base ;
- porter une unité ;
- porter un taux de perte ;
- indiquer le comportement de génération.

Comportements possibles confirmés :

- fixe ;
- calculée ;
- remplacée ;
- conditionnelle ;
- manuelle ;
- sélectionnée ;
- copiée à valider.

### RoutingBase

Gamme de base d'une famille produit fini.

Responsabilités :

- représenter la séquence industrielle de référence ;
- porter une version ;
- contenir des opérations ;
- servir de source aux gammes générées.

Relations :

- appartient à une ProductFamily ;
- contient plusieurs RoutingBaseOperation ;
- source de plusieurs RoutingGeneratedVersion.

### RoutingBaseOperation

Opération de gamme de base.

Responsabilités :

- porter un numéro d'ordre ;
- porter un nom d'opération ;
- référencer un workcenter ;
- porter une quantité de base ;
- porter un temps de base ;
- indiquer le comportement de génération.

### GenerationProfile

Profil de génération.

Responsabilités :

- définir les dimensions génératrices ;
- définir ce que le système peut générer ;
- porter les règles applicables ;
- porter un statut de gouvernance.

Statuts confirmés :

- Brouillon ;
- À valider ;
- Validé ;
- Bloqué ;
- Obsolète.

Relations :

- appartient à une ProductFamily ;
- contient plusieurs GenerationDimension ;
- contient ou référence des règles de génération.

### GenerationDimension

Dimension utilisée pour créer des variantes.

Dimensions confirmées :

- Taille ;
- Couleur ;
- Commande ;
- Composition.

Règle confirmée :

- minimum 2 dimensions : Taille x Couleur ;
- maximum 4 dimensions : Taille x Couleur x Commande x Composition.

### GenerationSession

Session de simulation ou de génération.

Responsabilités :

- mémoriser le profil utilisé ;
- mémoriser les valeurs d'entrée ;
- produire les variantes attendues ;
- regrouper les résultats ;
- regrouper les alertes ;
- permettre la traçabilité.

Modes :

- Simulation ;
- Génération.

### GeneratedVariant

Variante générée ou simulée.

Responsabilités :

- représenter une combinaison demandée ;
- porter les attributs de génération ;
- relier BOM générée, gamme générée et vues aplaties.

Exemples de dimensions :

- taille ;
- couleur ;
- commande ;
- composition.

### BomGeneratedVersion

Version de nomenclature générée.

Responsabilités :

- être dérivée d'une BomBase ;
- être liée à une GeneratedVariant ;
- porter un statut ;
- contenir des BomGeneratedLine ;
- conserver la traçabilité de génération.

### BomGeneratedLine

Ligne de nomenclature générée.

Responsabilités :

- porter le composant généré ou conservé ;
- porter le besoin net ;
- porter le besoin brut ;
- porter l'unité ;
- porter le taux de perte ;
- référencer la ligne source si elle existe ;
- référencer les traces de calcul.

### RoutingGeneratedVersion

Version de gamme générée.

Responsabilités :

- être dérivée d'une RoutingBase ;
- être liée à une GeneratedVariant ;
- porter un statut ;
- contenir des RoutingGeneratedOperation ;
- conserver la traçabilité.

### RoutingGeneratedOperation

Opération de gamme générée.

Responsabilités :

- porter l'opération ;
- porter le workcenter référencé ;
- porter la quantité ;
- porter le temps calculé ou copié ;
- référencer l'opération source si elle existe ;
- référencer les traces de calcul.

### FlattenedBom

Projection aplatie matière.

Responsabilités :

- consolider les besoins composants ;
- accélérer le CBN matière ;
- conserver le chemin source ;
- porter une version ou date de calcul ;
- porter un statut.

Règle :

- ce n'est pas la référence technique principale.

### FlattenedRouting

Projection aplatie charge.

Responsabilités :

- consolider les charges par workcenter ou workstation ;
- accélérer le calcul charge/capacité ;
- conserver le chemin source ;
- porter une version ou date de calcul ;
- porter un statut.

Règle :

- ce n'est pas la référence industrielle principale.

### CalculationTrace

Trace de calcul.

Responsabilités :

- expliquer chaque résultat généré ;
- conserver la source ;
- conserver la règle appliquée ;
- conserver les coefficients ;
- conserver les valeurs avant/après ;
- conserver les pertes ;
- conserver l'origine manuelle si applicable.

### GenerationAlert

Alerte de génération.

Responsabilités :

- signaler une règle absente ;
- signaler une donnée manquante ;
- signaler une génération incomplète ;
- signaler une validation nécessaire.

### ProposedComponent

Composant proposé automatiquement.

Responsabilités :

- proposer un composant selon les attributs générés ;
- permettre acceptation, remplacement, refus ou validation.

Statuts confirmés :

- Proposé ;
- Accepté ;
- Remplacé ;
- Refusé ;
- À valider.

### UnitOfMeasure

Unité de mesure.

Responsabilités :

- identifier l'unité utilisée ;
- permettre les calculs de quantité ;
- servir aux conversions.

### UnitConversionRule

Règle de conversion d'unité.

Responsabilités :

- convertir une unité source vers une unité cible ;
- permettre le calcul cohérent des vues aplaties.

## Référentiels externes référencés

### ArticleReference

Référence article minimale.

Rôle dans le MVP :

- permettre au module de référencer produits finis, semi-finis et composants.

Hors MVP :

- configurateur article complet.

### WorkcenterReference

Référence d'un centre de charge existant dans Axioplan.

Rôle :

- affecter les opérations ;
- agréger les charges.

### WorkstationReference

Référence d'un poste existant.

Rôle :

- poste préféré ou alternatif.

À confirmer pour MVP.

### CalendarReference

Référence de calendrier existant.

Rôle :

- capacité disponible pour la planification.

Hors MVP actuel sauf référence éventuelle.

## Relations principales

```text
ProductFamily
  -> BomBase
    -> BomBaseLine
  -> RoutingBase
    -> RoutingBaseOperation
  -> GenerationProfile
    -> GenerationDimension

GenerationSession
  -> GeneratedVariant
    -> BomGeneratedVersion
      -> BomGeneratedLine
      -> FlattenedBom
    -> RoutingGeneratedVersion
      -> RoutingGeneratedOperation
      -> FlattenedRouting
  -> CalculationTrace
  -> GenerationAlert
```

## Hors Domain Model actuel

- Duplication complète d'article.
- Matrice client complète.
- Import article.
- IA planning.
- Connexion Sage.
- Connexion Axioplan.
- Interface graphique.

