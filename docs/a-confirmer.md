# Questions a confirmer

## Donnees Sage / Axioplan

- Confirme : Sage, Axioplan reel et la base SQL reelle ne sont pas accessibles pour l'instant.
- A confirmer : source future des articles.
- A confirmer : source future des workcenters.
- A confirmer : source future des workstations.
- A confirmer : source future des calendriers.
- A confirmer : format de connexion Sage.
- A confirmer : base SQL cible.

## Exemple prod

- Confirme : le fichier `prod` fournit des operations, workcenters, quantites et temps visibles.
- A confirmer : signification de `0022605MFG00000365`.
- A confirmer : signification de `PE260268 / AH25PLUMIERE M MINT`.
- A confirmer : unite de `Time`.
- A confirmer : `Time` est-il un temps total pour 30 pieces ou un temps unitaire ?
- A confirmer : signification de `Besoin alloue = 152,094`.
- A confirmer : role de `Paquet`, `Code-barres` et `Package Number`.

## Donnees simulees MVP

- Simule : `FIL-MINT`.
- Simule : `VCOMP-STD`.
- Simule : `SACHET-STD`.
- Simule : libelles generiques des workcenters.
- Simule : BOM exemple du pull col rond.
- Simule : coefficients taille/couleur utilises dans les tests.
- Simule : profil `PROFILE_SIZE_COLOR` comme profil valide de demonstration.

Ces donnees ne doivent jamais etre presentees comme confirmees par Sage, Axioplan ou Montepull.

## Regles metier

- A confirmer : regles de codification article.
- A confirmer : liste officielle des familles produit fini.
- A confirmer : liste officielle des familles composants.
- A confirmer : coefficients taille/couleur reels.
- A confirmer : taux de perte par famille ou composant.
- A confirmer : politique CBN exacte par statut.
- A confirmer : zone gelee et regles associees.
- A confirmer : regles d'allocation Pegging (priorite OA/OF/stock, partiel, multi-source).
- A confirmer : profondeur cascade BOM Pegging (`PEGGING_MAX_CASCADE_LEVEL`).

## Application

- Supposé : SQLite est un choix temporaire de MVP local.
- Supposé : Python est un choix temporaire de MVP local.
- A confirmer : application web, desktop ou API.
- A confirmer : utilisateurs cibles du MVP.
- A confirmer : droits et profils utilisateurs.
- A confirmer : langue definitive de l'interface.

## Hors MVP actuel

- Configurateur article complet.
- IA de planification.
- Interface utilisateur.
- Connexion Sage.
- CBN complet.
- Gestion complete des calendriers.
