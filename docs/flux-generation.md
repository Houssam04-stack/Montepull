# Flux de generation MVP

## Objectif

Generer des nomenclatures et gammes derivees a partir d'une famille produit fini, d'une BOM de base, d'une gamme de base et d'un profil valide.

## Perimetre

### Confirme

- Le flux cible vient du document Configurateur Gammes & Nomenclatures.
- Le fichier `prod` confirme seulement les operations, workcenters, quantites et temps visibles.

### Simule

- La BOM utilisee pour tester le flux local est simulee.
- Les composants `FIL-MINT`, `VCOMP-STD` et `SACHET-STD` sont simules.
- Les coefficients de calcul sont simules tant qu'ils ne sont pas fournis par Montepull.

### Hors MVP actuel

- Configurateur article complet.
- IA planning.
- Interface utilisateur.
- CBN complet.
- Connexion Sage/Axioplan reelle.

## Flux

```text
1. Choisir une famille produit fini
2. Charger sa BOM de base
3. Charger sa gamme de base
4. Choisir un profil de generation valide
5. Saisir les dimensions demandees
6. Simuler les variantes
7. Controler les alertes
8. Generer les objets derives
9. Calculer les vues aplaties
10. Enregistrer les traces
```

## Premier cas MVP

Dimensions :

- taille ;
- couleur.

Exemple :

```text
Tailles : S, M, L
Couleurs : MINT, NOIR
Resultat : 6 variantes
```

Les dimensions `commande` et `composition` sont preparees dans le modele mais non obligatoires dans le premier flux MVP.

## Regles de calcul

### Besoin matiere

```text
besoin_net = besoin_base x coefficient_taille x coefficient_couleur
besoin_brut = besoin_net / (1 - taux_perte)
```

### Temps operation

```text
temps_genere = temps_base x coefficient_taille x coefficient_couleur
```

## Tracabilite obligatoire

Chaque ligne generee doit stocker :

- source ;
- variante ;
- regle appliquee ;
- valeur de base ;
- coefficients ;
- perte ;
- resultat ;
- origine manuelle si applicable.

## Regle de validation MVP

Un profil peut etre utilise pour generer des objets exploitables seulement si son statut est `VALIDATED`.

Les profils `DRAFT`, `TO_VALIDATE`, `BLOCKED` et `OBSOLETE` peuvent etre conserves dans la base, mais ne doivent pas produire de donnees utilisables par le CBN.
