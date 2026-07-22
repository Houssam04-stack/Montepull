# Modele de donnees MVP

## Confirmé

Le modele doit couvrir :

- articles produits finis, semi-finis et composants ;
- familles produit fini ;
- BOM de base ;
- gammes de base ;
- profils de generation ;
- BOM et gammes generees ;
- vues aplaties ;
- tracabilite.

## Supposé

- Le modele ci-dessous est volontairement normalise mais simple pour un MVP local SQLite.
- SQLite est un choix temporaire pour le MVP local.
- Python est un choix temporaire pour tester et executer la logique metier localement.

## Hors MVP actuel

- Configurateur article complet.
- IA planning.
- Interface utilisateur.
- Connexion Sage/Axioplan reelle.
- CBN complet.

## Referentiel article minimal

| Table | Role |
|---|---|
| article_categories | Categories larges : produit fini, semi-fini, composant, packaging, etc. |
| article_families | Familles metier : pull, cardigan, fil, vignette, etc. |
| articles | References article simulees. |
| attribute_definitions | Attributs configurables globaux. |
| attribute_options | Valeurs autorisees ou creees pour un attribut. |
| article_attribute_values | Valeurs d'attribut portees par un article. |

Ces tables ne constituent pas le Configurateur Articles complet. Elles forment seulement un noyau support minimal.

## Gammes & Nomenclatures

| Table | Role |
|---|---|
| product_families | Familles de produits finis generatrices. |
| bom_bases | Nomenclatures de base. |
| bom_base_lines | Lignes de nomenclature de base. |
| routing_bases | Gammes de base. |
| routing_base_operations | Operations de gamme de base. |
| workcenters | Centres de charge references. |
| units_of_measure | Unites de mesure utilisees par BOM, articles et conversions. |
| unit_conversion_rules | Conversions d'unite simulees ou futures. |
| generation_profiles | Profils de generation. |
| generation_profile_dimensions | Dimensions generatrices du profil. |
| consumption_coefficients | Coefficients de besoin matiere par attribut. |
| time_coefficients | Coefficients de temps par attribut. |
| bom_line_generation_rules | Regles applicables aux lignes BOM. |
| routing_operation_generation_rules | Regles applicables aux operations de gamme. |
| generation_sessions | Sessions de simulation ou generation. |
| generated_variants | Variantes generees. |
| proposed_components | Composants proposes automatiquement apres generation. |
| bom_generated_versions | Versions de BOM generees. |
| bom_generated_lines | Lignes de BOM generees. |
| routing_generated_versions | Versions de gamme generees. |
| routing_generated_operations | Operations de gamme generees. |
| flattened_bom_lines | Vue aplatie matiere. |
| flattened_routing_lines | Vue aplatie charge. |
| calculation_traces | Tracabilite des calculs. |
| generation_alerts | Alertes de generation. |

## Donnees confirmees et simulees

| Source | Statut | Utilisation |
|---|---|---|
| Documents de cadrage | Confirme | Concepts, flux, statuts et objets metier. |
| Fichier `prod` | Confirme partiellement | Operations, workcenters, quantites et temps visibles. |
| `FIL-MINT` | Simule | Composant matiere pour BOM exemple. |
| `VCOMP-STD` | Simule | Composant etiquette/vignette pour BOM exemple. |
| `SACHET-STD` | Simule | Composant packaging pour BOM exemple. |
| Coefficients tests | Simule | Verification des formules, pas regles metier reelles. |

## Statuts principaux

### Profil de generation

- DRAFT
- TO_VALIDATE
- VALIDATED
- BLOCKED
- OBSOLETE

### Objet genere

- DRAFT
- GENERATED
- GENERATED_WITH_WARNINGS
- TO_COMPLETE
- TO_VALIDATE
- VALIDATED
- OBSOLETE
- ERROR

## Regles MVP

- Un profil non valide ne cree pas d'objet exploitable par CBN.
- Une generation conserve toujours sa session source.
- Une ligne calculee conserve toujours une trace de calcul.
- Une vue aplatie est une projection calculee, pas la reference technique principale.
- Une modification de BOM, gamme, coefficient, unite ou perte doit permettre de recalculer les vues aplaties.
- Une vue aplatie doit porter un statut, une date de calcul, une source et une session de generation.
- Une trace de calcul doit identifier la source, la variante, la regle, les coefficients, l'unite et le resultat.
