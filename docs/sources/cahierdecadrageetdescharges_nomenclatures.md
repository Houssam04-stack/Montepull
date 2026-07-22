# Cahier de cadrage et des charges

## Nomenclature aplatie avec pegging sur achat, vente et OF

*Projet Smart Factory — Manufacture Mode*

**Version 0.2 — 6 juillet 2026**
**Auteur : Reda**
*Statut : draft — pour revue interne*

---

## Suivi du document

Ce cahier de cadrage et des charges (CCC) initialise la démarche projet. Il pose le contexte, arrête les objectifs, délimite le périmètre et formule les exigences fonctionnelles et non fonctionnelles associées à la construction d'une nomenclature aplatie assortie d'un pegging sur les flux d'achat, de vente et d'ordre de fabrication (OF), y compris la cascade multi-niveaux PF / SF / composant.

### Historique des versions

| Version | Date | Auteur | Nature de la révision |
|---|---|---|---|
| v0.1 | 06/07/2026 | Reda | Création — cadrage initial et exigences majeures |
| v0.2 | 06/07/2026 | Reda | Ajout §4.4 registres et cascade multi-niveaux, §4.5 politiques MTS/MTO/sous-traitance, §5.5 pegging PF/SF/composant (EF-40..EF-46), enrichissement §6 modele de donnees |

### Instances de validation

| Rôle | Instance | Décision attendue | Échéance |
|---|---|---|---|
| Sponsor | Direction generale | Validation périmètre et budget | T+2 sem. |
| Métier | Comite supply & production | Validation exigences fonctionnelles | T+3 sem. |
| SI | DSI / architecte SI | Validation modèle de données et intégration | T+4 sem. |
| Projet | Chef de projet | Arbitrages courants et suivi jalons | Permanent |

---

## 1. Contexte et enjeux

### 1.1 Contexte industriel

La manufacture évolue dans un secteur mode caractérisé par des cycles produits courts (collections saisonnières et capsules), une forte variabilité des matières et des fournitures, des séries de production réduites et une pression permanente sur le time-to-shelf. La chaîne produit associe une phase amont d'achats (tissus, accessoires, garnitures) à cycles longs et une phase aval de commercialisation à cycles très courts, avec des ordres de fabrication de petite à moyenne taille, souvent multi-références et articulés autour de semi-finis mutualisés.

Le système d'information actuel gère les nomenclatures multi-niveaux, les ordres d'achat, de vente et de fabrication de manière séparée. La lecture de bout en bout d'un besoin composant jusqu'à sa consommation finale, et symétriquement d'une commande client jusqu'à ses sources d'approvisionnement, n'est pas outillée : elle est reconstituée manuellement, avec un délai et un taux d'erreur incompatibles avec la cadence de décision imposée par le marché.

### 1.2 Enjeux métiers

- **Visibilité fin-a-fin :** relier chaque composant a ses demandes servantes (OV, OF PF, OF SF) et chaque demande a ses sources (stocks, OA, OF).
- **Réactivité aux aléas :** identifier immédiatement, en cas de rupture, glissement ou modification, la population de commandes ou d'OF impactés le long de la cascade, et déclencher les arbitrages.
- **Arbitrages proposés :** affecter les ressources rares (matières critiques, capacités atelier, OF SF partagés) aux demandes à plus fort enjeu (client stratégique, marge, échéance contractuelle).
- **Fiabilisation des engagements :** objectiver l'ATP/CTP au niveau composant et semi-fini plutôt qu'au seul niveau produit fini.
- **Support à la transformation smart factory :** constituer la base de données de pilotage exploitable par les couches d'analytics, de supervision et, à terme, de pilotage cybernétique evenementiel.

### 1.3 Enjeux SI

Constituer une donnée unifiée et normalisée, calculée périodiquement et à la demande, utilisable par l'ERP, le MES, les tableaux de bord et les modules de simulation ou d'ordonnancement, sans dupliquer les référentiels source (articles, nomenclatures, gammes).

---

## 2. Objectifs

### 2.1 Objectifs fonctionnels

- **Nomenclature aplatie :** produire, pour tout article fini ou semi-fini, la liste exhaustive de ses composants ultimes avec quantités cumulées, en gérant coefficients, pertes, variantes et alternatives, tout en préservant la traçabilité des SF traverses.
- **Pegging achat :** relier chaque ligne d'ordre d'achat (OA) aux demandes qu'elle sert (OF de tout niveau, OV, stock de sécurité) et inversement.
- **Pegging vente :** relier chaque ligne de commande client (OV) aux sources qui la couvrent (stock disponible, OF PF, cascade OF SF / OA).
- **Pegging OF :** relier chaque OF a ses OA de couverture composants et aux OV, OF PF ou OF SF qu'il sert.
- **Cascade multi-niveaux :** matérialiser et interroger la chaîne OV → OF PF → OF SF → OA composant sur profondeur de BOM quelconque.
- **Analyse d'impact :** a partir d'un aléa (retard OA, panne, refus qualité, modification OV), produire la liste triée des demandes impactées avec profondeur de propagation indiquée.

### 2.2 Objectifs techniques

- **Calcul incrémental :** recalcul complet nocturne et recalculs événementiels (delta) déclenchés par un événement métier significatif.
- **Traçabilité :** conserver l'historique des liens de pegging (versions) pour audit et analyse de causes.
- **Performance :** temps de réponse compatibles avec un usage interactif sur les vues d'impact et d'arbitrage.
- **Ouverture :** API stable pour consommation par MES, BI et modules analytiques ultérieurs.

### 2.3 Objectifs métriques (indicateurs de succès)

| Indicateur | Cible v1 | Méthode de mesure |
|---|---|---|
| Couverture pegging OV → sources | ≥ 98 % des lignes actives | Ratio lignes peggees / total lignes ouvertes |
| Couverture pegging OA → demandes | ≥ 95 % | Idem cote achat |
| Couverture cascade OV → OA composant | ≥ 95 % sur profondeur complète | Ratio chaines completes / OV actifs |
| Fraîcheur (delta ≤ événement) | ≤ 15 min | Horodatage événement → publication delta |
| Temps de réponse vue d'impact | ≤ 3 s pour 10 000 lignes | Mesure applicative P95 |
| Réduction delai reponse ATP | -50 % vs baseline | Chronométrage avant/après |
| Taux d'erreurs d'arbitrage détectées a posteriori | -70 % vs baseline | Audit hebdomadaire |

---

## 3. Périmètre

### 3.1 Périmètre fonctionnel

Inclus : construction et maintien de la nomenclature aplatie de tout article géré en production (PF et SF de tout niveau) ; calcul et publication des liens de pegging pour tout OA, OV et OF actif ; consolidation de la cascade multi-niveaux ; vues et exports associés ; API de consultation ; connecteurs vers ERP source.

Hors périmètre v1 : re-conception du référentiel articles ou de la nomenclature multi-niveaux ; re-conception du calcul MRP ; ordonnancement atelier ; pegging simule (what-if) au-delà d'un mode consultation ; gestion des retours et SAV.

### 3.2 Périmètre applicatif

| Système | Rôle | Interaction attendue |
|---|---|---|
| ERP | Source de vérité articles, BOM, OA, OV, OF, stocks | Lecture périodique et événementielle (webhook / bus) |
| MES | Reporting production, consommations réelles | Alimentation en événements de consommation composant |
| PLM | Définition et versions nomenclature | Notification changement BOM |
| WMS | Stocks physiques et emplacements | Lecture stocks disponibles pour pegging |
| BI / analytics | Consommateur des vues consolidées | API et vues matérialisées |

### 3.3 Périmètre données

- Articles finis, semi-finis, matières et fournitures gérés en nomenclature.
- Ordres d'achat, ordres de vente et ordres de fabrication, ouverts ou clôturés sur la fenêtre de 12 mois glissants (paramètre).
- Stocks disponibles, réservés et en cours (par emplacement, par niveau PF/SF/composant).
- Événements métiers déclenchant un recalcul (création, modification, annulation, réception, expédition, changement de statut, modification de date).

### 3.4 Exclusions

- Nomenclatures purement commerciales ou marketing sans traduction pour la production.
- Articles hors production (frais généraux, consommables non nomenclatures).
- Pegging inter-sites tant qu'un seul site est concerné en v1.

---

## 4. Définitions et concepts clés

### 4.1 Nomenclature aplatie

La nomenclature aplatie est la représentation à un seul niveau d'une nomenclature à N niveaux : elle liste, pour un article pere, l'ensemble des composants ultimes (feuilles) requis pour produire une unité, avec les quantités cumulées obtenues par produit des coefficients de chaque niveau, corrigées des taux de perte, des variantes actives, et des règles de substitution ou d'alternative retenues.

Elle sert de socle au pegging : c'est à ce niveau que les besoins bruts en composants ultimes sont exprimés, appariés aux OA correspondants, et remontés au produit fini. Le chemin d'origine (chaine des SF traverses) est préservé en attribut pour permettre la remontée vers les SF intermédiaires.

### 4.2 Pegging

Le pegging est la traçabilité bidirectionnelle des liens entre demande et couverture : a toute quantité demandée est associée une ou plusieurs quantités couvrantes ; a toute quantité couvrante est associée une ou plusieurs quantités demandées. Il s'exprime au niveau ligne (OA, OV, OF) et couvre trois flux : achat, vente, fabrication.

| Type | Sens | Question métier typique |
|---|---|---|
| Pegging descendant (demande → source) | OV → OF PF → OF SF → OA / stock | « Comment est couverte ma commande client ? » |
| Pegging ascendant (source → demande) | OA → OF SF → OF PF → OV | « A quoi sert ma réception ? » « A qui sert cet OF ? » |
| Pegging horizontal (OF → OF) | OF SF → OF PF | « Quels OF PF dépendent de cet OF SF ? » |

### 4.3 Variantes de pegging

- **Statique :** photographie a un instant T, publiée en batch.
- **Dynamique :** recalculée événementiellement a chaque modification d'une ligne source ou demande.
- **Ferme vs prévisionnel :** le pegging peut relier une demande ferme a une source prévisionnelle (OA planifie, OF SF non lance), avec un statut explicite du lien.

### 4.4 Registres du pegging et cascade multi-niveaux (PF / SF / composant)

Le pegging s'exprime sur deux registres distincts et complémentaires. Le premier, structurel, découle de la nomenclature (BOM) : il exprime la relation définie entre produit fini (PF), semi-fini (SF) et composant ultime. La nomenclature aplatie en est la forme condensée : elle projette le PF directement sur ses composants ultimes en écrasant les niveaux intermédiaires, tout en conservant, par le chemin d'origine, la traçabilité des SF traverses. Le second registre, opérationnel, découle des ordres : il exprime la relation d'exécution entre OV, OF PF, OF SF et OA composants, matérialisée par les liens de pegging.

La chaîne opérationnelle complète se décrit ainsi : OV → OF de PF → OF de SF (un ou plusieurs niveaux) → OA de composants (ou stock composant). Chaque maillon peut être résolu de deux façons : par consommation d'un stock existant, ou par déclenchement d'un ordre dédié (OA ou OF). Le système doit rendre cette chaîne explicite et interrogeable dans les deux sens : descendant (couverture d'une commande) et ascendant (usage d'un OA ou d'un OF).

| Niveau | Origine des besoins | Modes de couverture |
|---|---|---|
| Produit fini (PF) | OV, stock de sécurité PF, prévisions | Stock PF, OF PF |
| Semi-fini (SF) niveau n | OF PF ou OF SF de niveau superieur, stock SF cible | Stock SF, OF SF, sous-traitance |
| Composant ultime | OF de tout niveau consommant ce composant | Stock composant, OA (ferme ou prévisionnel) |

### 4.5 Politiques de gestion par niveau

La cascade de pegging dépend de la politique de gestion appliquée à chaque SF. Un SF géré en make-to-stock (MTS) est anonyme : les OF sont déclenchés par un point de commande ou un plan directeur, et ils alimentent un stock de consommation banalisé. Un SF géré en make-to-order (MTO) est dédié : chaque OF SF est rattaché à un OF PF spécifique, avec un pegging ferme établi dès la création. Le système doit gérer les deux modes et permettre le changement de mode par article, avec effet à la nouvelle date de création d'OF.

- **MTS (make-to-stock) :** OF SF banalisé ; pegging ascendant vers OF PF établi au moment de la réservation ou de la consommation réelle.
- **MTO (make-to-order) :** OF SF dédié ; pegging ascendant établi dès la création de l'OF SF ; propagation immédiate des glissements.
- **Sous-traitance :** OF SF adossé à un OA de prestation ; double pegging (OA prestation + OA composants fournis) ; délais et jalons spécifiques.

### 4.6 Terminologie retenue

| Acronyme | Définition |
|---|---|
| OA | Ordre d'achat (Purchase Order) |
| OV | Ordre de vente / commande client (Sales Order) |
| OF | Ordre de fabrication (Manufacturing Order) |
| BOM | Bill of Materials — nomenclature |
| PF | Produit fini — article de plus haut niveau vendu au client |
| SF | Semi-fini — article intermédiaire produit puis consommé par un OF de niveau supérieur |
| Composant ultime | Article acheté ou matière brute, feuille de nomenclature |
| MTS | Make-to-Stock — production sur stock, SF anonyme |
| MTO | Make-to-Order — production à la commande, SF dédié à un OF pere |
| ATP | Available-to-Promise — disponible à la promesse |
| CTP | Capable-to-Promise — capable à la promesse |
| MRP | Material Requirements Planning — calcul des besoins |

---

## 5. Exigences fonctionnelles

Les exigences sont identifiées par un code EF-XX. Le niveau de priorité suit l'échelle MoSCoW : M (must), S (should), C (could), W (won't v1).

### 5.1 Nomenclature aplatie

| Code | Exigence | Prio | Origine |
|---|---|---|---|
| EF-01 | Le système doit produire, pour tout article pere actif (PF ou SF), la liste aplatie de ses composants ultimes avec quantité unitaire cumulée, taux de perte appliqué et statut du composant. | M | Métier prod. |
| EF-02 | Le système doit gérer les nomenclatures à variantes (options, tailles, coloris) et sélectionner la variante active selon la date d'effet et les paramètres OF/OV. | M | PLM / Prod. |
| EF-03 | Le système doit tracer, dans la nomenclature aplatie, le chemin d'origine multi-niveaux de chaque composant (chaîne des SF traverses, coefficients par niveau). | M | Métier |
| EF-04 | Le système doit gérer les composants alternatifs (substitution) selon les règles de priorité et disponibilité. | S | Achats |
| EF-05 | Le système doit détecter et signaler les cycles ou incohérences dans les nomenclatures source. | M | SI |
| EF-06 | Le système doit reconstruire la nomenclature aplatie en delta lors d'une modification BOM notifiée par le PLM. | M | SI / PLM |

### 5.2 Pegging sur achat (OA)

| Code | Exigence | Prio | Origine |
|---|---|---|---|
| EF-10 | Chaque ligne d'OA doit être reliée à une ou plusieurs demandes servies (OF de tout niveau, OV, stock de sécurité) avec quantité et statut du lien. | M | Achats |
| EF-11 | Le système doit répartir la quantité d'une réception OA sur plusieurs demandes selon des règles configurables (FIFO, priorité client, date de besoin). | M | Achats / Métier |
| EF-12 | Le système doit distinguer les liens fermes (OA valide) des liens prévisionnels (OA planifie). | M | SI |
| EF-13 | Le système doit produire, pour tout OA, la liste des OV et OF impactés (tous niveaux) en cas de retard, refus ou annulation. | M | Métier |
| EF-14 | Le système doit permettre le re-pegging manuel d'un OA sur une demande alternative avec traçabilité de l'intervention. | S | Métier |

### 5.3 Pegging sur vente (OV)

| Code | Exigence | Prio | Origine |
|---|---|---|---|
| EF-20 | Chaque ligne d'OV doit être reliée à ses sources de couverture (stock PF, OF PF, cascade OF SF / OA composant) avec quantité et échéance. | M | Commercial |
| EF-21 | Le système doit calculer un ATP et un CTP par ligne OV à partir des liens de pegging et de la disponibilité composant et semi-fini. | M | Commercial / Prod. |
| EF-22 | Le système doit signaler les lignes OV dont la couverture est partielle, tardive ou reposant sur des sources non fermes (à tout niveau de la cascade). | M | Commercial |
| EF-23 | Le système doit gérer la priorisation client (segment, marge, contrat) dans l'affectation des sources rares aux OV. | M | Direction |
| EF-24 | Le système doit gérer les allotissements (une ligne OV couverte par plusieurs sous-livraisons ordonnancées). | S | Logistique |

### 5.4 Pegging sur OF

| Code | Exigence | Prio | Origine |
|---|---|---|---|
| EF-30 | Chaque OF doit être relié en amont à ses OA de couverture composants et/ou à ses OF SF de couverture (ferme ou prévisionnel). | M | Prod. |
| EF-31 | Chaque OF doit être relié en aval à l'OV, l'OF de niveau supérieur ou le stock qu'il sert. | M | Prod. |
| EF-32 | Le système doit consolider, pour tout OF, l'état de couverture composants et SF (complet, partiel, en attente, en rupture). | M | Prod. |
| EF-33 | Le système doit propager, en cas de glissement de date d'un OF, l'impact sur les OF et OV dépendants (tous niveaux). | M | Ordo. / Métier |
| EF-34 | Le système doit permettre la simulation d'un OF non lance (pegging what-if) sans figer les liens. | C | Métier |

### 5.5 Pegging multi-niveaux (PF / SF / composant)

Cette famille d'exigences formalise la cascade opérationnelle OV → OF PF → OF SF → OA composant et son pendant descendant, ainsi que la coexistence des politiques MTS et MTO décrites au §4.5.

| Code | Exigence | Prio | Origine |
|---|---|---|---|
| EF-40 | Le système doit relier chaque OF de PF à ses OF de SF de couverture (mode MTO) ou à une réservation de stock SF (mode MTS), avec quantité, echéance et statut du lien. | M | Prod. |
| EF-41 | Le système doit relier chaque OF de SF à ses OA composants de couverture (ou stock composant) et, en amont, à l'OF PF ou aux OF SF qu'il sert. | M | Prod. / Achats |
| EF-42 | Le système doit consolider la chaîne complète de pegging OV → OF PF → OF SF → OA composant sur une profondeur de nomenclature quelconque, et l'exposer en vue unique par ligne OV. | M | Commercial / Prod. |
| EF-43 | Le système doit gérer par article la politique de gestion (MTS, MTO, sous-traitance) et adapter les règles de pegging en conséquence, avec effet à la création d'OF. | M | Métier / SI |
| EF-44 | Le système doit propager toute perturbation (retard, rupture, refus qualité, modification) le long de la cascade multi-niveaux et produire la population impactée côté OV, avec profondeur d'impact indiquée. | M | Métier |
| EF-45 | Le système doit permettre le partage d'un OF SF entre plusieurs OF PF (mode MTS) et gérer les réallocations en cas d'arbitrage, avec traçabilité des versions de lien. | S | Prod. |
| EF-46 | Le système doit gérer le double pegging des OF sous-traités : OA de prestation + OA (ou mouvement) des composants fournis, avec consolidation dans la fiche OF. | S | Achats / Prod. |

### 5.6 Vues et arbitrages

- **Vue « fiche composant » :** pour un composant donné, l'ensemble des OA le couvrant et des OF le consommant (tout niveau), avec un timeline visuel.
- **Vue « fiche SF » :** OF SF ouverts, OF PF servis, OA composants attendus, stock disponible, mode MTS/MTO.
- **Vue « fiche OV » :** ligne par ligne, sources de couverture (stock, OF PF, cascade SF/composant), dates promises vs. dates couvertes, statut du pegging.
- **Vue « fiche OF » :** composants attendus, OA associés, OV/OF aval servis, chaîne amont/aval de niveau, jalons production.
- **Vue « cascade multi-niveaux » :** arbre OV → OF PF → OF SF → OA composant, code couleur statut et retard cumulé.
- **Vue « analyse d'impact » :** a partir d'un événement (retard OA, panne, refus qualité), population impactée triée par priorité, profondeur d'impact et suggestions d'arbitrage.
- **Vue « arbitrages en attente » :** liste des points de décision ou plusieurs demandes concurrentes pour une même source rare (composant, OF SF, capacité).

---

## 6. Modele de donnees (macro)

### 6.1 Entités principales

| Entité | Contenu essentiel |
|---|---|
| Article | Code, désignation, type (PF / SF / composant ultime), unite, statut, politique de gestion (MTS / MTO / sous-traitance), attributs mode (saison, collection) |
| BOM Ligne | Article pere, article composant, coefficient, taux de perte, variante, dates d'effet, niveau BOM |
| BOM Aplatie | Article pere (PF ou SF), composant ultime, quantité cumulée, chemin d'origine (chaine des SF traverses), version, horodatage |
| OA Ligne | Fournisseur, article, quantite, date attendue, statut, prix, nature (composant ou prestation sous-traitance) |
| OV Ligne | Client, article (PF), quantité, date promise, priorité, marge, statut |
| OF_Ligne | Article (PF ou SF), niveau, quantité, date début/fin planifiée, statut, ressource principale, mode (MTS / MTO / sous-traite) |
| Stock | Article, emplacement, quantité disponible, réservée, en attente qualité, niveau (PF / SF / composant) |
| Lien Pegging | Source (type, id, ligne, niveau), Demande (type, id, ligne, niveau), quantité, statut (ferme / prévisionnel), origine (règle / manuel), date effet, version, chaîne parent (pour tracer la cascade) |
| Événement | Type, entité concernée, horodatage, payload, statut de traitement |

### 6.2 Règles de gestion transverses

- Un lien de pegging est immuable une fois publié ; toute révision créer une nouvelle version, la précédente restant tracée pour audit.
- Un composant ou un SF peut être servi partiellement par plusieurs sources ; la somme des quantités liées doit égaler la quantité demandée à la clôture.
- Les liens prévisionnels sont progressivement remplacés par des liens fermes à mesure que les OA et OF passent en statut confirmé.
- Un lien peut être manuel (arbitrage utilisateur) ou automatique (réglé) ; l'origine est tracée.
- La chaîne parent d'un lien conserve la référence du lien de niveau supérieur, permettant la reconstitution de la cascade sans jointures cumulées à la volée.

### 6.3 Volumétrie estimée (dimensionnement v1)

| Objet | Volumétrie annuelle | Pic instantané |
|---|---|---|
| Articles gérés en BOM (dont ~30 % SF) | ≈ 25 000 | n/a |
| OA ouverts | ≈ 12 000 lignes | 3 500 |
| OV ouverts | ≈ 40 000 lignes | 12 000 |
| OF actifs (PF + SF) | ≈ 8 000 | 2 500 |
| Liens de pegging (tous niveaux) | ≈ 700 000 | 200 000 actifs |
| Evénements / jour | ≈ 20 000 | 1 000 / heure |

---

## 7. Exigences non fonctionnelles

| Code | Catégorie | Exigence |
|---|---|---|
| ENF-01 | Performance | Recalcul complet nocturne ≤ 2 h ; recalcul delta evenementiel ≤ 15 min ; requêtes interactives P95 ≤ 3 s ; reconstitution cascade complète ≤ 5 s par OV. |
| ENF-02 | Disponibilité | Service disponible 24/6 avec fenêtre de maintenance planifiée hebdomadaire ; RTO ≤ 4 h, RPO ≤ 15 min. |
| ENF-03 | Intégrité | Aucun lien orphelin toléré ; contrôle de cohérence quotidien avec alerte automatique ; vérification cascade (chaîne parent bien formée). |
| ENF-04 | Traçabilité | Toute création, modification ou suppression de lien tracée avec auteur, horodatage et source (événement, batch, utilisateur). |
| ENF-05 | Sécurité | Controle d'acces base roles ; distinction lecture, arbitrage, administration ; journalisation des actions sensibles. |
| ENF-06 | Auditabilité | Rejet possible d'un état de pegging à une date passée sur la fenêtre de rétention (12 mois). |
| ENF-07 | Scalabilité | Architecture supportant un doublement de volumétrie sans re-conception. |
| ENF-08 | Observabilité | Journalisation, métriques et traces exposées ; tableau de bord de santé du service. |
| ENF-09 | Réversibilité | Format de données documente, exports standards (CSV, Parquet, API) permettant la migration. |

---

## 8. Intégration SI et interfaces

### 8.1 Interfaces amont (ingestion)

- **ERP :** extraction batch quotidienne (articles, BOM, OA, OV, OF, stocks) et flux evenementiel via bus de messages ou webhooks pour créations et mises à jour.
- **PLM :** notification des changements de BOM et publication des nouvelles versions.
- **MES :** événements de consommation composant et d'avancement OF (tous niveaux).
- **WMS :** état stock disponible / réservé par emplacement, par niveau.

### 8.2 Interfaces aval (publication)

- **API REST :** consultation de la nomenclature aplatie, des liens de pegging, de la cascade multi-niveaux et des vues d'impact.
- **Bus d'événements :** publication des changements significatifs (nouvelles couvertures, ruptures détectées, arbitrages requis).
- **Exports :** extraits CSV et Parquet pour BI et analytics.

### 8.3 Cadence et modes

| Flux | Cadence | Mode |
|---|---|---|
| Recalcul complet | Quotidien (nuit) | Batch |
| Delta evenementiel | Continu | Evenementiel |
| Alignement PLM | A chaque changement BOM | Evenementiel |
| Alignement WMS | Toutes les 15 min | Micro-batch |
| Publication événements aval | Continu | Evenementiel |

---

## 9. Livrables, jalons et gouvernance

### 9.1 Livrables projet

- Cahier de cadrage et des charges (le présent document).
- Spécifications fonctionnelles détaillées (SFD).
- Spécifications techniques (architecture, modèle de données, interfaces).
- Cahier de recettes (jeux d'essais, cas de tests, critères d'acceptation).
- Documentation d'exploitation et guide utilisateur.

### 9.2 Jalons prévisionnels

| Jalon | Livrable clé | Échéance | Décision |
|---|---|---|---|
| J0 | CCC valide | T0 | Go conception |
| J1 | SFD validées | T0 + 6 sem. | Go developpement |
| J2 | Prototype nomenclature aplatie + cascade PF/SF/composant | T0 + 10 sem. | Revue technique |
| J3 | Pilote pegging OA/OV/OF multi-niveaux sur périmètre restreint | T0 + 16 sem. | Go extension |
| J4 | Recette utilisateur | T0 + 22 sem. | Go mise en production |
| J5 | Mise en service v1 | T0 + 26 sem. | Passage exploitation |

### 9.3 RACI synthétique

| Activité | Sponsor | Métier | Chef projet | DSI | Intégrateur |
|---|---|---|---|---|---|
| Validation CCC | A | C | R | C | I |
| Spécifications fonctionnelles | I | C | A | C | R |
| Specifications techniques | I | I | A | C | R |
| Developpement et integration | I | I | A | C | R |
| Recette et qualification | I | R | A | C | C |
| Mise en service | A | C | R | C | C |

*Convention : R = Réalise, A = approuvé, C = consulte, I = informe.*

---

## 10. Critères d'acceptation

La v1 est acceptée si l'ensemble des critères ci-dessous est satisfait sur le périmètre pilote pendant deux semaines glissantes.

- Toutes les exigences fonctionnelles de priorité M sont couvertes et validées par cas de tests documentés, y compris la cascade multi-niveaux (EF-40..EF-44).
- Toutes les ENF sont mesurées et respectées sur l'environnement de recette.
- Les indicateurs de succès (§2.3) sont atteints ou dépassés sur la fenêtre de mesure.
- Aucun défaut bloquant ni critique ouverte ; défauts majeurs plafonnés à cinq et sous plan d'action documenté.
- La documentation utilisateur et d'exploitation est livrée, revue et validée.
- Un plan de bascule et de retour arrière est validé et testé.

---

## 11. Risques, hypothèses, dépendances

### 11.1 Risques

| Risque | Prob. | Impact | Parade |
|---|---|---|---|
| Qualité insuffisante des BOM source (variantes, dates d'effet, niveaux SF manquants) | Élevée | Élevé | Audit BOM en amont ; plan de fiabilisation PLM pilote |
| Résistance métier à la formalisation des règles d'arbitrage | Moyenne | Élevé | Ateliers de co-conception ; règles versionnées et audit trail |
| Ambiguïté MTS/MTO sur SF partagés entre plusieurs PF | Élevée | Élevé | Formalisation par article de la politique ; ergonomie explicite ; règles de réallocation tracées |
| Retards d'intégration cote ERP (webhooks, latence) | Moyenne | Moyen | Fallback batch ; contrat d'interface documente |
| Volumétrie sous-estimée sur les pics saisonniers et la cascade multi-niveaux | Moyenne | Moyen | Dimensionnement 2x ; test de charge amont recette |
| Confusion entre pegging ferme et prévisionnel côté commercial | Élevée | Moyen | Statut explicite du lien ; formation ciblée ; ergonomie des vues |

### 11.2 Hypothèses

- Le référentiel articles et la structure BOM ERP (avec niveaux SF explicites) sont maintenus par le PLM ; aucun re-référencement n'est requis pour la v1.
- L'ERP expose un flux evenementiel (bus ou webhooks) sur les objets OA, OV, OF et stocks ; à défaut, un polling toutes les 5 minutes est acceptable en v1.
- Les politiques de gestion (MTS / MTO / sous-traitance) sont documentales au niveau article et stables sur la fenêtre projet.
- Les règles de priorisation client sont formalisables et stables sur la fenêtre projet.

### 11.3 Dépendances

- Disponibilité effective du bus d'événements (ou équivalent) cote ERP.
- Fiabilisation préalable des dates d'effet, variantes et niveaux SF dans le PLM.
- Alignement des politiques MTS/MTO par article, validé par la direction production.
- Alignement des règles de priorisation client et de segmentation par la direction commerciale.

---

## 12. Annexes

### 12.1 Glossaire complémentaire

| Terme | Définition |
|---|---|
| Feuille de nomenclature | Composant ultime non lui-même produit, généralement une matière ou fourniture acheter |
| Substitution | Remplacement d'un composant par un composant alternatif équivalent selon règles |
| Lien ferme | Lien de pegging associé à un OA ou OF confirmé |
| Lien prévisionnel | Lien de pegging reposant sur une source planifiée non encore confirmée |
| Chaîne parent | Référence du lien de pegging de niveau supérieur, permettant de reconstituer la cascade |
| Cascade multi-niveaux | Chaine complete OV -> OF PF -> OF SF -> OA composant, sur profondeur de BOM quelconque |
| Analyse d'impact | Population de demandes ou d'OF affectés par un événement source donne, avec profondeur indiquée |
| Arbitrage | Choix d'affectation d'une source rare entre plusieurs demandes concurrentes |

### 12.2 Références

- Standards MRP II et méthodologie APICS pour les concepts de nomenclature, de pegging et de politiques MTS/MTO.
- Documentation ERP client (spécifications d'interface, catalogue d'événements) - à référencer dans la SFD.
- Politique de sécurité SI et normes internes de gestion des données.

### 12.3 Reste à préciser (avant SFD)

- Périmètre exact des variantes gérées en v1 (tailles, coloris, options).
- Cartographie des articles SF avec politique de gestion cible (MTS / MTO / sous-traitance).
- Règles de priorisation client formalisées et validées par la direction commerciale.
- Règles de réallocation d'un OF SF partagé entre plusieurs OF PF (arbitrage automatique ou manuel).
- Fenêtre de rétention historique retenue (12 mois par défaut).
- Contrat d'interface ERP (catalogue d'événements, latence garantie, mode dégradé).
