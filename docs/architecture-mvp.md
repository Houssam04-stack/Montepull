# Architecture MVP Axioplan

## Statut du document

Document d'architecture initiale construit a partir du contexte officiel projet et des documents de cadrage fournis.

Mise a jour de direction : le module devra pouvoir etre integre dans Axioplan, application .NET. Les choix Python et SQLite restent des supports temporaires de MVP local, d'analyse et de tests. La trajectoire cible est documentee dans `dotnet-architecture-proposee.md`.

## Confirmé

- Entreprise : Montepull.
- Domaine : textile.
- Application cible : Axioplan.
- Le projet est un MVP de stage d'un mois.
- L'application locale doit fonctionner sans acces a la base SQL reelle, a Sage ou a Axioplan.
- La base locale doit etre simulee mais realiste.
- Sage, Axioplan reel et la base SQL reelle ne sont pas accessibles pour l'instant.
- Le premier module a developper est le Configurateur Gammes & Nomenclatures.
- Les modules suivants sont le Configurateur Articles puis l'IA de planification.
- Les modules doivent rester independants et pouvoir communiquer plus tard avec Sage.
- Le module Gammes & Nomenclatures doit gerer :
  - familles de produits finis ;
  - nomenclatures de base ;
  - gammes de base ;
  - profils de generation ;
  - generation automatique de variantes ;
  - generation de nomenclatures ;
  - generation de gammes ;
  - vues aplaties ;
  - tracabilite ;
  - preparation du CBN.

## Supposé

- SQLite est retenu pour le MVP local car il est simple, rapide a installer et suffisant pour une base simulee.
- Python est retenu pour structurer la logique metier locale, car il permet de construire rapidement un prototype testable.
- Ces choix ne representent pas l'architecture finale Axioplan.
- L'architecture cible est .NET, avec Domain, Application, Infrastructure, API, Database et Tests.
- Une API ou une interface pourra etre ajoutee ensuite sans changer le coeur metier.
- Le module Gammes & Nomenclatures aura besoin d'un referentiel article minimal avant le developpement complet du Configurateur Articles.

## A confirmer

- Stack definitive de l'application finale.
- Format final attendu : web, desktop, API seule ou application mixte.
- Base cible future : SQL Server, PostgreSQL, base Sage, autre.
- Connecteurs Sage disponibles.
- Semantique exacte des champs issus de l'exemple `prod`.
- Unite exacte des temps de gamme.
- Regles reelles de codification article.

## Principe d'architecture

L'architecture est decoupee en trois couches simples :

```text
Interface future
  -> Services applicatifs par module
  -> Domaine metier
  -> Repositories SQL
  -> Base locale SQLite
```

Pour le MVP, l'interface peut etre ajoutee plus tard. Le plus important est de stabiliser :

- le modele de donnees ;
- les regles de generation ;
- les vues aplaties ;
- la tracabilite.

## Donnees confirmees et simulees

### Confirme

- Le fichier `prod` confirme uniquement les informations visibles suivantes :
  - sequence des operations ;
  - noms des operations ;
  - codes workcenters ;
  - quantite affichee ;
  - temps affiche.
- Les documents de cadrage confirment les concepts metier : BOM, gammes, profils, vues aplaties, statuts et tracabilite.

### Simule

- Les composants `FIL-MINT`, `VCOMP-STD` et `SACHET-STD` sont des donnees simulees necessaires au MVP.
- Les libelles des workcenters sont simules quand seul le code est fourni.
- Les BOM exemple sont simulees tant que la vraie nomenclature Sage/Axioplan n'est pas disponible.
- Les coefficients taille/couleur utilises dans les tests sont des exemples de calcul, pas des coefficients metier confirmes.

## Modules

### Core

Socle partage minimal :

- connexion base ;
- transactions ;
- statuts communs ;
- types simples ;
- journalisation technique.

### Module 1 - Gammes & Nomenclatures

Module prioritaire.

Responsabilites :

- gerer les familles produit fini ;
- gerer les BOM de base ;
- gerer les gammes de base ;
- gerer les profils de generation ;
- simuler la generation ;
- generer BOM et gammes derivees ;
- calculer les vues aplaties ;
- tracer les calculs.

### Module 2 - Articles

Hors MVP actuel en tant que configurateur complet.

Un noyau article minimal existe seulement pour permettre au module Gammes & Nomenclatures de fonctionner localement.

Responsabilites MVP limitees :

- stocker les articles simules ;
- distinguer produit fini, semi-fini et composant ;
- porter quelques attributs : taille, couleur, commande, composition.

Hors MVP actuel :

- matrice client complete ;
- duplication article ;
- gouvernance complete des valeurs ;
- import externe ;
- ecrans de configuration article.

### Module 3 - IA de planification

Hors MVP actuel.

Responsabilites futures :

- exploiter les articles, BOM, gammes et vues aplaties ;
- proposer des scenarios de planification ;
- utiliser les charges par workcenter.

## Flux cible MVP

```text
Famille produit fini
  -> BOM de base
  -> Gamme de base
  -> Profil de generation valide
  -> Selection tailles/couleurs
  -> Simulation
  -> Generation
  -> BOM derivee
  -> Gamme derivee
  -> Vues aplaties
  -> Tracabilite
```

## Choix de simplicite

- Pas de microservices.
- Pas de bus d'evenements.
- Pas de moteur de workflow complexe.
- Pas de dependance obligatoire a Sage pendant le MVP.
- Pas d'IA tant que les donnees structurees ne sont pas stables.

## Limites volontaires du MVP

- Generation limitee d'abord a Taille x Couleur.
- Commande et composition preparees dans le modele, mais pas obligatoires dans le premier lot.
- Workcenters seulement references.
- Calendriers non recalcules.
- CBN complet non implemente : seules les vues aplaties le preparent.
- Pas d'interface utilisateur dans cette phase.
- Pas de configurateur article complet dans cette phase.
- Pas d'IA planning dans cette phase.
