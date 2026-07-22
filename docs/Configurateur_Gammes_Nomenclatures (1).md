# AXIOPLAN
## Document de cadrage

# Configurateur Gammes & Nomenclatures

*Nomenclatures de base, gammes de base, génération à la volée, aplatissement CBN et traçabilité*

**Version de cadrage fonctionnel destinée à l'équipe projet. Le document formalise les décisions validées avant préparation des prompts de développement.**

| Information | Valeur |
|---|---|
| Solution | Axioplan |
| Module | Configurateur Gammes & Nomenclatures |
| Statut | Cadrage validé - prêt pour découpage en prompts |
| Date | 27/06/2026 |
| Périmètre | Nomenclatures, gammes, règles de génération, multi-unités/mesures, vues aplaties et CBN |

---

## 1. Objectif du module

Le module Gammes & Nomenclatures Axioplan doit permettre de générer, versionner, contrôler et aplatir des nomenclatures et gammes à partir de modèles de base par famille de produit fini. L'objectif principal est d'accélérer le CBN en évitant l'explosion complète des structures multi-niveaux à chaque calcul, tout en conservant la traçabilité technique.

**La structure détaillée sert à concevoir et maîtriser. La structure aplatie sert à calculer vite et piloter.**

| Objet | Rôle |
|---|---|
| Nomenclature multi-niveau | Référence technique : structure produit, semi-finis, composants, quantités et règles. |
| Gamme détaillée | Référence industrielle : opérations, séquence, temps, ressources existantes. |
| Nomenclature aplatie | Projection calculée pour accélérer le CBN matière. |
| Gamme aplatie | Projection calculée pour accélérer le calcul de charge et capacité. |
| Pegging | Traçabilité des besoins vers demandes, OF, opérations et versions. |

---

## 2. Principes généraux validés

- Une nomenclature de base et une gamme de base sont définies par famille de produit fini.
- Les profils de génération sont multiples, paramétrables et doivent être validés.
- Le système peut générer une seule nomenclature/gamme ou plusieurs à la volée.
- L'explosion combinatoire est maîtrisée : minimum 2 dimensions, maximum 4 dimensions.
- Les besoins unitaires et temps standards peuvent être calculés par coefficients de majoration/minoration.
- Si aucune règle ne s'applique, la ligne est copiée, saisie, sélectionnée, bloquée ou marquée à compléter selon la politique.
- Les composants proposés automatiquement restent validables.
- Les multi-unités et multi-mesures dépendent de la famille article.
- Les vues aplaties accélèrent le CBN et le calcul charge/capacité.
- Les workcenters, workstations et calendriers existent déjà dans Axioplan et sont seulement référencés.

---

## 3. Nomenclature et gamme de base par famille de produit fini

La génération ne doit pas dépendre uniquement d'un article source. Le référentiel de départ est la famille de produit fini, à laquelle sont rattachées une nomenclature de base et une gamme de base.

```
Famille produit fini
  -> Nomenclature de base
  -> Gamme de base
  -> Profil de génération validé
  -> Articles / BOM / gammes dérivés
```

| Famille produit fini | Nomenclature de base | Gamme de base |
|---|---|---|
| Pull col rond | BOM_BASE_PULL_COL_ROND | GAM_BASE_PULL_COL_ROND |
| Cardigan | BOM_BASE_CARDIGAN | GAM_BASE_CARDIGAN |
| Gilet | BOM_BASE_GILET | GAM_BASE_GILET |
| Robe maille | BOM_BASE_ROBE_MAILLE | GAM_BASE_ROBE_MAILLE |

---

## 4. Profils de génération validés

Un profil de génération définit ce que le système peut générer, avec quels attributs, quelles règles de calcul et quels contrôles. Il constitue la clé de gouvernance de la génération automatique.

| Profil | Usage | Résultat possible |
|---|---|---|
| Taille x Couleur | Profil standard produit fini | Articles, BOM et gammes par variante taille/couleur |
| Taille x Couleur x Commande | Articles spécifiques commande | Variantes rattachées à une demande client |
| Taille x Couleur x Composition | Articles dépendants composition matière | BOM ajustées selon composition |
| Taille x Couleur x Commande x Composition | Cas client spécifique complet | Génération maximale maîtrisée |
| BOM seule | Préparer les besoins composants | Nomenclature générée sans gamme |
| Gamme seule | Préparer les charges | Gamme générée sans BOM |
| Simulation | Prévisualiser avant création | Aucun objet définitif créé |

| Statut profil | Signification |
|---|---|
| Brouillon | Profil en préparation, utilisable en simulation seulement. |
| À valider | Profil complet mais en attente de validation métier. |
| Validé | Profil autorisé pour génération et CBN selon politique. |
| Bloqué | Profil interdit temporairement. |
| Obsolète | Profil remplacé par une nouvelle version. |

**Règle : seuls les profils validés peuvent créer des nomenclatures/gammes utilisables par le CBN.**

---

## 5. Attributs générateurs et explosion combinatoire maîtrisée

La base de génération est volontairement limitée pour éviter l'explosion combinatoire inutile. Le minimum retenu est Tailles x Couleurs. Le maximum retenu est Tailles x Couleurs x Commandes x Compositions.

| Niveau | Dimensions | Exemple |
|---|---|---|
| Minimum | Tailles x Couleurs | S, M, L x Bleu, Noir |
| Intermédiaire 1 | Tailles x Couleurs x Commandes | Variantes par commande client |
| Intermédiaire 2 | Tailles x Couleurs x Compositions | Variantes par composition matière |
| Maximum | Tailles x Couleurs x Commandes x Compositions | Cas client spécifique complet |

**Le système ne doit pas générer toutes les combinaisons possibles par défaut. Il génère les combinaisons demandées, validées ou nécessaires au CBN.**

---

## 6. Règles de calcul des besoins unitaires

Les besoins unitaires des composants peuvent être calculés automatiquement à partir d'une quantité de base et de coefficients applicables aux attributs de l'article. Les premiers attributs concernés sont la taille et la couleur, mais le mécanisme doit rester générique.

```
Besoin net unitaire = Besoin de base x coefficient taille x coefficient couleur x autres coefficients applicables
Besoin brut unitaire = Besoin net unitaire / (1 - taux de perte)
```

| Exemple | Valeur |
|---|---|
| Besoin base fil taille M couleur Bleu | 0,500 kg |
| Coefficient taille L | 1,10 |
| Coefficient couleur Noir | 1,02 |
| Besoin net calculé | 0,500 x 1,10 x 1,02 = 0,561 kg |
| Perte | 5 % |
| Besoin brut | 0,561 / 0,95 = 0,591 kg |

| Type de comportement ligne BOM | Description | Exemple |
|---|---|---|
| Fixe | Quantité copiée sans changement | 1 carteline par pièce |
| Calculée | Quantité recalculée par coefficients | Fil principal |
| Remplacée | Composant remplacé selon attribut | Fil bleu -> fil noir |
| Conditionnelle | Ligne ajoutée/supprimée selon règle | Renfort taille XL |
| Manuelle | Saisie obligatoire utilisateur | Accessoire spécifique |
| Sélectionnée | Choix dans une liste autorisée | Packaging client |
| Copiée à valider | Copie conservée mais validation requise | Composant sensible |

---

## 7. Règles de génération des gammes

Les gammes dérivées sont générées à partir d'une gamme de base. Les opérations, temps standards et affectations peuvent être copiés, recalculés, remplacés ou validés selon les profils de génération.

```
Temps opération généré = Temps de base x coefficient taille x coefficient couleur x autres coefficients applicables
```

| Opération | Temps base | Politique possible |
|---|---|---|
| Tricotage | 18 min/pièce | Calculée par taille/couleur |
| Assemblage | 12 min/pièce | Calculée ou fixe selon famille |
| Repassage | 4 min/pièce | Fixe ou ajustée |
| Contrôle | 3 min/pièce | Fixe ou client |
| Emballage | 2 min/pièce | Client / packaging |

**Le configurateur de gamme ne recrée pas les workcenters, workstations ou calendriers. Il référence les objets existants Axioplan.**

---

## 8. Référentiels industriels existants : workcenters, workstations, calendriers

Les workcenters, workstations et calendriers sont considérés comme existants dans Axioplan. Le module génère des opérations qui pointent vers ces référentiels afin d'alimenter le calcul de charge et la planification.

| Objet existant | Usage dans le configurateur |
|---|---|
| Workcenter | Affectation principale des opérations et agrégation de charge. |
| Workstation | Poste préféré ou alternatif pour exécution. |
| Calendrier workcenter | Capacité disponible déjà rattachée au workcenter. |
| Calendrier workstation si existant | Disponibilité plus fine d'un poste. |

| À ne pas recréer | À référencer / enrichir |
|---|---|
| Workcenter | RoutingOperation.WorkcenterId |
| Workstation | RoutingOperation.WorkstationId / alternatives |
| Calendar | Calendrier utilisé par le moteur de planification |
| WorkcenterCalendar | Charge aplatie par workcenter existant |

---

## 9. Multi-unités et multi-mesures selon famille article

Chaque article ou composant peut être géré en multi-unités et multi-mesures. Les mesures attendues dépendent de la famille article : poids, longueur, surface, volume, diamètre, titrage, conditionnement, contenance, etc.

| Famille article | Unités / mesures possibles |
|---|---|
| Produit fini | Pièce, poids brut, poids net, volume emballé, pièces/carton |
| Fil | Kg, cône, carton, poids/cône, mètres/kg, titrage |
| Tissu | Mètre, rouleau, laize, grammage, surface, poids rouleau |
| Bouton | Pièce, sachet, carton, diamètre, poids/pièce |
| Vignette / étiquette / carteline | Pièce, paquet, rouleau, dimensions, langue |
| Packaging | Pièce, carton, dimensions, volume, contenance |

| Notion | Exemple | Usage |
|---|---|---|
| Unité | pièce, kg, mètre, cône, paquet | Quantifier les flux |
| Mesure technique | poids net, largeur, diamètre, volume | Décrire l'article selon sa famille |
| Attribut configurable | taille, couleur, client, commande | Générer ou différencier les variantes |

---

## 10. Composants proposés automatiquement en aval de la génération

Certains articles composants peuvent être proposés automatiquement dans la nomenclature en fonction des attributs générés. C'est le cas de la vignette de composition, des étiquettes taille, cartelines ou composants packaging spécifiques client.

```
Sélection : tailles S, M, L et couleurs Bleu, Noir
Proposition automatique : une vignette de composition par combinaison taille x couleur
```

| Statut composant proposé | Signification |
|---|---|
| Proposé | Le système suggère le composant à partir des règles. |
| Accepté | L'utilisateur confirme son intégration. |
| Remplacé | Un autre composant est choisi. |
| Refusé | Le composant n'est pas nécessaire. |
| À valider | Validation technique ou client requise. |

---

## 11. Cas sans règle automatique

Toutes les lignes ne seront pas couvertes par des règles. Le profil de génération doit préciser le comportement à appliquer en cas de règle absente.

| Stratégie | Description |
|---|---|
| Copier la valeur de base | La quantité ou le temps source est conservé. |
| Demander une saisie manuelle | L'utilisateur renseigne la valeur. |
| Demander une sélection | L'utilisateur choisit dans une liste autorisée. |
| Utiliser une valeur par défaut | Le système applique une valeur paramétrée. |
| Bloquer la génération | La génération ne peut pas aboutir sans règle. |
| Marquer à compléter | L'objet est créé avec statut incomplet. |

---

## 12. Nomenclatures et gammes aplaties pour accélérer le CBN

La nomenclature multi-niveau et la gamme détaillée restent les références. Les vues aplaties sont des projections calculées, versionnées et traçables destinées au CBN et au calcul de charge.

```
Demande -> Article / variante -> Nomenclature aplatie -> Besoins composants -> Stock / achats / OF -> Pegging
```

| Vue aplatie | Contenu clé | Usage |
|---|---|---|
| Nomenclature aplatie | Composants consolidés, besoins nets/bruts, unités, coefficients, pertes, chemins BOM | CBN matière rapide |
| Gamme aplatie | Charges par workcenter/workstation, temps unitaires, setup, transfert, chemins opérations | Calcul charge/capacité rapide |

| Déclencheur de recalcul | Recalcul vue aplatie ? |
|---|---|
| Nouvelle version BOM | Oui |
| Modification coefficient taille/couleur | Oui |
| Modification conversion unité | Oui |
| Modification taux de perte | Oui |
| Modification gamme / temps standard | Oui |
| Modification stock | Non |
| Nouvelle commande client | Selon article spécifique commande |

---

## 13. Statuts, validation et règles avant CBN

Les nomenclatures et gammes générées doivent avoir un statut. Le CBN ne doit pas consommer des données incomplètes ou non validées selon la politique métier et la zone Axioplan.

| Statut | Sens |
|---|---|
| Brouillon | Objet en préparation. |
| Générée | Toutes les règles obligatoires ont été appliquées. |
| Générée avec alertes | Objet calculé mais certaines lignes doivent être revues. |
| À compléter | Données obligatoires manquantes. |
| À valider | Validation métier requise. |
| Validée | Utilisable par CBN selon politique. |
| Obsolète | Remplacée par une nouvelle version. |
| Erreur | Génération impossible. |

**Règle : le CBN rapide utilise uniquement les nomenclatures/gammes validées ou générées sans alerte si la politique métier l'autorise. En zone gelée, le contrôle doit être plus strict.**

---

## 14. Traçabilité des calculs

Chaque ligne générée doit pouvoir expliquer son résultat. La traçabilité est indispensable pour l'audit, l'acceptation métier, le CBN et le pegging.

| Élément tracé | Exemple |
|---|---|
| Source | BOM_BASE_PULL_COL_ROND_V1 |
| Article / variante | PULL-123-L-NOIR |
| Règle appliquée | SIZE_COLOR_CONSUMPTION |
| Quantité base | 0,500 kg |
| Coefficient taille | L = 1,10 |
| Coefficient couleur | Noir = 1,02 |
| Perte | 5 % |
| Besoin net / brut | 0,561 kg / 0,591 kg |
| Origine manuelle | Saisie utilisateur si règle absente |

---

## 15. Objets conceptuels à prévoir

Les objets ci-dessous cadrent le module sans préjuger de l'implémentation exacte. Ils pourront être adaptés selon les entités déjà présentes dans Axioplan.

| Domaine | Objets conceptuels |
|---|---|
| BOM | BomBase, BomBaseLine, BomGeneratedVersion, BomLineGenerationPolicy, FlattenedBomLine |
| Gamme | RoutingBase, RoutingBaseOperation, RoutingGeneratedVersion, RoutingOperationGenerationRule, FlattenedRoutingLine |
| Profils | GenerationProfile, GenerationProfileAttribute, GenerationProfileRule, GenerationProfileStatus |
| Règles | AttributeConsumptionRule, AttributeConsumptionCoefficient, AttributeTimeRule, AttributeTimeCoefficient |
| Unités / mesures | UnitOfMeasure, ArticleUnit, ArticleFamilyMeasurement, ArticleMeasurementValue, UnitConversionRule |
| Traçabilité | GenerationSession, GenerationResult, GenerationAlert, CalculationTrace |
| Pegging / CBN | MaterialRequirement, CapacityRequirement, PeggingLink |

---

## 16. Flux cible de génération

```
Famille produit fini
  -> BOM / gamme de base
  -> Profil de génération validé
  -> Sélection attributs : tailles, couleurs, commandes, compositions
  -> Prévisualisation / simulation
  -> Application règles automatiques
  -> Saisie ou sélection manuelle des lignes non couvertes
  -> Contrôle unités, mesures, conversions, pertes, ressources
  -> Validation
  -> BOM / gamme générées et versionnées
  -> Vues aplaties
  -> CBN matière + calcul charge
  -> Pegging
```

---

## 17. Synthèse des décisions validées

| Sujet | Décision validée |
|---|---|
| Base de génération | Une BOM et une gamme de base par famille de produit fini. |
| Profils | Multiples, paramétrables et validés. |
| Génération | Unitaire ou multiple à la volée. |
| Combinatoire | Minimum tailles x couleurs ; maximum tailles x couleurs x commandes x compositions. |
| Règles | Coefficients de majoration/minoration pour besoins et temps. |
| Sans règle | Copie, saisie, sélection, défaut, blocage ou à compléter. |
| Composants automatiques | Proposés puis validables, exemple vignette de composition. |
| Multi-unités/mesures | Paramétrées selon famille article. |
| CBN | Utilise les vues aplaties validées pour accélération. |
| Ressources | Workcenters, workstations et calendriers existants sont référencés. |
| Traçabilité | Calculs, règles, coefficients, versions et saisies manuelles tracés. |
