# Business Rules - Gammes & Nomenclatures

## Objectif

Regrouper les règles métier confirmées, simulées ou à confirmer qui serviront de référence au moteur de génération.

## Convention

Chaque règle indique son statut :

- Confirmé : présent dans les documents métier.
- Simulé : utile au MVP local mais non confirmé métier.
- À confirmer : nécessaire avant usage réel.

## Règles de périmètre

### BR-PER-001

Statut : Confirmé.

SI le besoin concerne le MVP actuel,
ALORS le périmètre est limité au module Gammes & Nomenclatures.

### BR-PER-002

Statut : Confirmé.

SI une fonctionnalité concerne le configurateur article complet,
ALORS elle est hors MVP actuel.

### BR-PER-003

Statut : Confirmé.

SI une fonctionnalité concerne l'IA de planification,
ALORS elle est hors MVP actuel.

### BR-PER-004

Statut : Confirmé.

SI une fonctionnalité nécessite Sage ou Axioplan réel,
ALORS elle n'est pas développée dans le MVP actuel.

## Règles de base de génération

### BR-GEN-001

Statut : Confirmé.

SI une génération est lancée,
ALORS elle doit partir d'une famille de produit fini.

### BR-GEN-002

Statut : Confirmé.

SI une famille produit fini est utilisée pour générer,
ALORS elle doit être rattachée à une BOM de base et/ou une gamme de base selon le profil.

### BR-GEN-003

Statut : Confirmé.

SI un profil de génération n'est pas Validé,
ALORS il ne peut pas créer de BOM ou gamme utilisable par le CBN.

### BR-GEN-004

Statut : Confirmé.

SI un profil est en Brouillon,
ALORS il est utilisable en simulation seulement.

### BR-GEN-005

Statut : Confirmé.

SI un profil est Bloqué ou Obsolète,
ALORS il ne doit pas être utilisé pour générer des objets exploitables.

## Règles de combinatoire

### BR-COMBI-001

Statut : Confirmé.

SI une génération utilise les dimensions minimales,
ALORS elle utilise Taille x Couleur.

### BR-COMBI-002

Statut : Confirmé.

SI une génération utilise les dimensions maximales,
ALORS elle ne dépasse pas Taille x Couleur x Commande x Composition.

### BR-COMBI-003

Statut : Confirmé.

SI des valeurs existent dans les référentiels,
ALORS le système ne génère pas toutes les combinaisons possibles par défaut.

### BR-COMBI-004

Statut : Confirmé.

SI des variantes sont générées,
ALORS seules les combinaisons demandées, validées ou nécessaires au CBN sont produites.

## Règles BOM

### BR-BOM-001

Statut : Confirmé.

SI une ligne BOM est fixe,
ALORS la quantité est copiée sans changement.

### BR-BOM-002

Statut : Confirmé.

SI une ligne BOM est calculée,
ALORS le besoin net est calculé par coefficients applicables.

### BR-BOM-003

Statut : Confirmé.

SI un taux de perte existe,
ALORS le besoin brut est calculé à partir du besoin net.

Formule confirmée :

```text
Besoin brut unitaire = Besoin net unitaire / (1 - taux de perte)
```

### BR-BOM-004

Statut : Confirmé.

SI une ligne BOM est remplacée,
ALORS le composant source peut être remplacé selon attribut.

### BR-BOM-005

Statut : Confirmé.

SI une ligne BOM est conditionnelle,
ALORS elle peut être ajoutée ou supprimée selon règle.

### BR-BOM-006

Statut : Confirmé.

SI une ligne BOM est manuelle,
ALORS une saisie utilisateur est obligatoire.

### BR-BOM-007

Statut : Confirmé.

SI une ligne BOM est sélectionnée,
ALORS l'utilisateur choisit dans une liste autorisée.

### BR-BOM-008

Statut : Confirmé.

SI une ligne BOM est copiée à valider,
ALORS la copie est conservée mais nécessite validation.

## Règles gamme

### BR-ROUT-001

Statut : Confirmé.

SI une gamme dérivée est générée,
ALORS elle part d'une gamme de base.

### BR-ROUT-002

Statut : Confirmé.

SI une opération est calculée,
ALORS son temps est calculé par coefficients applicables.

Formule confirmée :

```text
Temps opération généré = Temps de base x coefficient taille x coefficient couleur x autres coefficients applicables
```

### BR-ROUT-003

Statut : Confirmé.

SI une opération référence un workcenter,
ALORS le configurateur ne recrée pas ce workcenter.

### BR-ROUT-004

Statut : Confirmé.

SI une opération référence une workstation ou un calendrier,
ALORS le configurateur référence les objets existants Axioplan.

À confirmer pour MVP :

- niveau exact de référence workstation/calendrier.

## Règles en absence de règle automatique

### BR-MISSING-001

Statut : Confirmé.

SI aucune règle automatique ne s'applique,
ALORS la stratégie définie par le profil doit être appliquée.

### BR-MISSING-002

Statut : Confirmé.

SI la stratégie est Copier la valeur de base,
ALORS la quantité ou le temps source est conservé.

### BR-MISSING-003

Statut : Confirmé.

SI la stratégie est Saisie manuelle,
ALORS l'utilisateur doit renseigner la valeur.

### BR-MISSING-004

Statut : Confirmé.

SI la stratégie est Sélection,
ALORS l'utilisateur choisit dans une liste autorisée.

### BR-MISSING-005

Statut : Confirmé.

SI la stratégie est Valeur par défaut,
ALORS le système applique une valeur paramétrée.

### BR-MISSING-006

Statut : Confirmé.

SI la stratégie est Blocage,
ALORS la génération ne peut pas aboutir sans règle.

### BR-MISSING-007

Statut : Confirmé.

SI la stratégie est À compléter,
ALORS l'objet peut être créé avec statut incomplet.

## Règles de composants proposés

### BR-PC-001

Statut : Confirmé.

SI certains composants dépendent des attributs générés,
ALORS ils peuvent être proposés automatiquement.

### BR-PC-002

Statut : Confirmé.

SI une vignette de composition dépend de Taille x Couleur,
ALORS le système peut proposer une vignette par combinaison.

### BR-PC-003

Statut : Confirmé.

SI un composant est proposé,
ALORS il peut prendre les statuts Proposé, Accepté, Remplacé, Refusé ou À valider.

## Règles de vues aplaties

### BR-FLAT-001

Statut : Confirmé.

SI une BOM générée est prête pour calcul,
ALORS une nomenclature aplatie peut être calculée.

### BR-FLAT-002

Statut : Confirmé.

SI une gamme générée est prête pour calcul,
ALORS une gamme aplatie peut être calculée.

### BR-FLAT-003

Statut : Confirmé.

SI une vue aplatie est créée,
ALORS elle reste une projection calculée et non la référence technique principale.

### BR-FLAT-004

Statut : Confirmé.

SI une nouvelle version BOM est créée,
ALORS la vue aplatie doit être recalculable.

### BR-FLAT-005

Statut : Confirmé.

SI un coefficient taille/couleur est modifié,
ALORS la vue aplatie doit être recalculable.

### BR-FLAT-006

Statut : Confirmé.

SI une conversion d'unité est modifiée,
ALORS la vue aplatie doit être recalculable.

### BR-FLAT-007

Statut : Confirmé.

SI un taux de perte est modifié,
ALORS la vue aplatie doit être recalculable.

### BR-FLAT-008

Statut : Confirmé.

SI un stock est modifié,
ALORS cela ne déclenche pas le recalcul de la vue aplatie.

## Règles de validation et CBN

### BR-CBN-001

Statut : Confirmé.

SI une BOM ou gamme est incomplète ou non validée,
ALORS elle ne doit pas être consommée par le CBN selon la politique métier.

### BR-CBN-002

Statut : Confirmé.

SI une BOM ou gamme est Validée,
ALORS elle est utilisable par CBN selon politique métier.

### BR-CBN-003

Statut : Confirmé.

SI une BOM ou gamme est Générée sans alerte,
ALORS elle peut être utilisable par CBN si la politique métier l'autorise.

### BR-CBN-004

Statut : À confirmer.

SI le calcul est en zone gelée,
ALORS le contrôle doit être plus strict.

La règle exacte reste à confirmer.

## Règles de traçabilité

### BR-TRACE-001

Statut : Confirmé.

SI une ligne est générée,
ALORS elle doit pouvoir expliquer son résultat.

### BR-TRACE-002

Statut : Confirmé.

SI un calcul est appliqué,
ALORS la trace doit contenir source, variante, règle, quantité ou temps de base, coefficients, perte et résultat.

### BR-TRACE-003

Statut : Confirmé.

SI une valeur est saisie manuellement,
ALORS l'origine manuelle doit être tracée.

## Règles issues du configurateur article utilisées comme support

### BR-ART-001

Statut : Confirmé pour le cadrage article, support MVP seulement.

SI une valeur saisie est utilisée pour générer un code, détecter un doublon, générer une nomenclature ou créer un lien de pegging,
ALORS elle doit être nettoyée, formatée et normalisée avant usage.

### BR-ART-002

Statut : Confirmé pour le cadrage article, hors configurateur complet.

SI un article est manipulé dans le MVP,
ALORS il peut être produit fini, semi-fini ou composant.

## Règles simulées MVP

### BR-SIM-001

Statut : Simulé.

SI le MVP local a besoin d'un composant matière,
ALORS `FIL-MINT` peut être utilisé comme composant simulé.

### BR-SIM-002

Statut : Simulé.

SI le MVP local a besoin d'un composant vignette,
ALORS `VCOMP-STD` peut être utilisé comme composant simulé.

### BR-SIM-003

Statut : Simulé.

SI le MVP local a besoin d'un composant packaging,
ALORS `SACHET-STD` peut être utilisé comme composant simulé.

### BR-SIM-004

Statut : Simulé.

SI les tests doivent vérifier les formules,
ALORS des coefficients taille/couleur simulés peuvent être utilisés.

