# Decoupage des modules

## Confirmé

L'ordre de developpement est :

1. Configurateur Gammes & Nomenclatures
2. Configurateur Articles
3. IA de planification

## Module 1 - Configurateur Gammes & Nomenclatures

### Lot 1 - Referentiels MVP

- Familles produit fini.
- Articles simules.
- Workcenters.
- Unites.
- Tailles et couleurs.

### Lot 2 - Gammes de base

- Creer une gamme de base par famille.
- Creer les operations de gamme.
- Referencer les workcenters.
- Stocker temps et quantites.

### Lot 3 - Nomenclatures de base

- Creer une BOM de base par famille.
- Ajouter composants.
- Gerer quantite, unite, perte.
- Preparer les comportements de ligne : fixe, calculee, remplacee, conditionnelle, manuelle.

### Lot 4 - Profils de generation

- Creer un profil.
- Associer les dimensions generatrices.
- Gerer les statuts : brouillon, a valider, valide, bloque, obsolete.
- Autoriser la generation seulement avec un profil valide.

### Lot 5 - Simulation et generation

- Selectionner tailles et couleurs.
- Previsualiser les combinaisons.
- Generer les variantes demandees.
- Generer BOM et gammes derivees.
- Tracer les calculs.

### Lot 6 - Vues aplaties

- Calculer les composants consolides.
- Calculer les charges par workcenter.
- Conserver la source et le chemin de calcul.

## Module 2 - Configurateur Articles

### MVP support uniquement

- Article.
- ArticleCategory.
- ArticleFamily.
- AttributeDefinition.
- AttributeOption.
- ArticleAttributeValue.

### Hors premier module

- Matrice client complete.
- Duplication avancee.
- Gouvernance complete des valeurs.
- Import externe.

## Module 3 - IA de planification

### Hors MVP initial

- Optimisation.
- Suggestions IA.
- Scenarios de charge.
- Arbitrage automatique.

## Regle de dependance

Le module Gammes & Nomenclatures peut lire le referentiel article minimal.

Le module Articles ne doit pas dependre du module Gammes & Nomenclatures.

Le module IA de planification dependra plus tard des deux premiers modules.

