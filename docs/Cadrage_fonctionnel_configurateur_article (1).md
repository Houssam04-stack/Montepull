# Cadrage fonctionnel
## Configurateur Article Axioplan

### Document de cadrage pour l'équipe projet

| Champ | Valeur |
|---|---|
| Solution | Axioplan |
| Module | Configurateur article |
| Périmètre | Articles produits finis, semi-finis et composants |
| Statut | Cadrage fonctionnel initial |
| Version | V0.1 |

**Objectif du document**

- Formaliser le cadrage article avant la rédaction des prompts de développement.
- Partager une vision commune entre métier, produit et équipe technique.
- Définir les règles de création, génération, duplication, attributs, matrices client et gouvernance des valeurs.

---

## Sommaire

1. Objectif et périmètre
2. Modes de création article
3. Structure de configuration
4. Catégories et familles article
5. Attributs configurables réutilisables
6. Matrice d'utilisation par client
7. Sélection, création de valeurs et formatage
8. Duplication d'article
9. Exemple métier : vignette de composition
10. Objets conceptuels retenus
11. Règles de gestion à valider
12. Prochaines étapes

---

## 1. Objectif et périmètre

Le configurateur article Axioplan doit permettre de créer, générer ou dupliquer des articles de manière contrôlée, paramétrable et traçable. Il constitue le socle du futur configurateur de nomenclature multi-niveau, de l'affichage aplati et du pegging des besoins.

| Type article | Exemples | Rôle dans Axioplan |
|---|---|---|
| Produit fini | Pull, gilet, cardigan, robe | Article vendu, demandé par le client ou piloté par prévision. |
| Semi-fini | Corps, manche, col, panneau tricoté | Élément fabriqué entrant dans un produit fini. |
| Composant | Fil, bouton, vignette, carteline, étiquette, sachet | Élément acheté, consommé ou intégré dans une nomenclature. |

**Principe directeur**

- L'article ne doit pas être un simple code stock.
- Il doit porter une logique configurable, réutilisable dans la nomenclature, la gamme, la planification, le stock, les OF et le pegging.

---

## 2. Modes de création article

| Mode | Description | Règle principale |
|---|---|---|
| Manuel | L'utilisateur crée l'article directement. | Contrôle minimal des champs obligatoires et du code. |
| Configuré | L'article est généré à partir d'une catégorie, d'une famille et d'attributs. | Application de la matrice client et des règles de génération. |
| Dupliqué | L'article est créé depuis un article source. | Nouveau code obligatoire et traçabilité vers l'article source. |
| Importé | L'article provient d'un fichier ou système externe. | Nettoyage, validation et mapping nécessaires avant intégration. |

---

## 3. Structure de configuration

Le configurateur repose sur une chaîne de décision simple :

**Catégorie article → Famille article → Attributs configurables → Matrice client → Règles de génération → Article généré**

| Niveau | Description |
|---|---|
| Catégorie article | Classement large : produit fini, semi-fini, matière principale, étiquetage, packaging… |
| Famille article | Comportement métier de l'article : pull, corps, vignette de composition, carteline… |
| Attributs configurables | Données utilisées pour saisir, générer, codifier ou décrire l'article. |
| Matrice client | Règles spécifiques par client et famille article. |
| Article généré | Référence réelle exploitable dans les stocks, nomenclatures, OF et pegging. |

---

## 4. Catégories et familles article

| Catégorie | Familles possibles |
|---|---|
| Produit fini | Pull, cardigan, robe, gilet |
| Semi-fini | Corps, manche, col, panneau |
| Matière principale | Fil, tissu, doublure |
| Accessoire | Bouton, zip, cordon |
| Étiquetage | Vignette composition, étiquette taille, étiquette marque, carteline |
| Packaging | Sachet, carton, sticker client |
| Service | Prestation, sous-traitance, contrôle externe |

La famille article définit les règles de saisie, génération, codification, désignation, contrôle, duplication et intégration éventuelle dans la nomenclature.

---

## 5. Attributs configurables réutilisables

Les attributs ne doivent pas être codés en dur dans les écrans. Ils sont définis dans un référentiel global, puis rattachés à plusieurs familles et matrices client.

| Attribut global | Exemples d'utilisation |
|---|---|
| Taille | Vignette de composition, carteline, étiquette taille, article fini, semi-fini. |
| Couleur | Vignette, carteline, article fini, fil principal, packaging client. |
| Commande client | Traçabilité, génération d'articles spécifiques, pegging. |
| Code article produit fini | Lien avec l'article parent, nomenclature, carteline, packaging. |
| Composition matière | Vignette de composition, fiche technique, contrôle qualité. |
| Code entretien | Vignette réglementaire, contrôle qualité. |
| Langue | Vignette, carteline, packaging export. |
| Pays de destination | Étiquetage réglementaire, packaging, documentation. |

| Objet conceptuel | Rôle |
|---|---|
| AttributeDefinition | Définition globale de l'attribut : code, libellé, type, source, formatage par défaut. |
| ArticleFamilyAttribute | Usage par défaut de l'attribut dans une famille article. |
| ConfigurationInput | Valeur réellement saisie, sélectionnée, formatée et normalisée dans une session. |

---

## 6. Matrice d'utilisation par client

La matrice d'utilisation doit être spécifique par client. Deux clients peuvent avoir des règles différentes pour une même famille article.

**Client × Famille article × Attribut**

La matrice client définit pour chaque attribut : visibilité, caractère obligatoire, modifiabilité, mono/multi-sélection, création de nouvelles valeurs, validation, rôle dans la génération, le code article, la désignation, la nomenclature et le pegging.

| Attribut | Client A / Vignette composition | Client B / Vignette composition |
|---|---|---|
| Commande client | Obligatoire, code, description, pegging | Obligatoire, code, description, pegging |
| Code article PF | Obligatoire, code, description, pegging | Obligatoire, description, pegging |
| Taille | Obligatoire, multi, génératrice | Obligatoire, multi, génératrice |
| Couleur | Obligatoire, multi, génératrice | Optionnelle, multi, génératrice |
| Composition | Obligatoire, à valider | Obligatoire, à valider |
| Code entretien | Obligatoire, sélection contrôlée | Obligatoire, sélection contrôlée |
| Langue | Optionnelle, non génératrice | Obligatoire, multi, génératrice |
| Pays destination | Non utilisé | Obligatoire |

**Règle d'héritage recommandée**

- Paramétrage spécifique commande, si défini.
- Sinon paramétrage client + saison.
- Sinon paramétrage client.
- Sinon paramétrage famille par défaut.
- Sinon paramétrage attribut global.

---

## 7. Sélection, création de valeurs et formatage

### 7.1 Modes de sélection

| Mode | Description | Exemples |
|---|---|---|
| Sélection contrôlée | L'utilisateur choisit dans une liste existante uniquement. | Commande client, client, code entretien, unité, devise, pays. |
| Sélection + création à la volée | L'utilisateur sélectionne des valeurs existantes ou saisit de nouvelles valeurs. | Taille, couleur, composition, variante client. |

### 7.2 Nettoyage et formatage obligatoires

Toute donnée saisie doit être nettoyée, formatée et normalisée avant d'être enregistrée, comparée ou utilisée dans une codification.

| Traitement | Règle |
|---|---|
| Espaces | Supprimer les espaces début/fin et remplacer les espaces multiples par un seul espace. |
| Espaces internes | Les supprimer si l'attribut l'exige, par exemple pour les tailles ou codes techniques. |
| Casse | Majuscule, minuscule ou casse titre selon l'attribut. |
| Accents | Conserver pour l'affichage, supprimer ou translittérer pour les codes techniques si nécessaire. |
| Caractères interdits | Supprimer ou refuser selon les règles de l'attribut. |
| Normalisation | Créer une valeur normalisée pour la détection de doublons. |
| Code technique | Créer un code propre utilisable dans la codification article. |

| Saisie brute | Valeur affichée | Valeur normalisée | Code technique |
|---|---|---|---|
| " bleu marine " | Bleu marine | BLEU MARINE | BLEU-MARINE |
| " xxl " | XXL | XXL | XXL |
| " lav30 - sechNon " | LAV30-SECHNON | LAV30-SECHNON | LAV30-SECHNON |
| " 70% laine / 30% acrylique " | 70% laine / 30% acrylique | 70% LAINE / 30% ACRYLIQUE | Selon règle attribut |

**Règle non négociable**

- La valeur brute saisie ne doit jamais servir directement à créer une option, générer un code article, détecter un doublon, générer une nomenclature ou créer un lien de pegging.

### 7.3 Gouvernance des nouvelles valeurs

| Statut | Sens |
|---|---|
| Brouillon | Valeur créée mais non encore validée. |
| À valider | Valeur utilisable provisoirement, mais à contrôler. |
| Validée | Valeur officielle du référentiel. |
| Bloquée | Valeur interdite à l'usage. |
| Fusionnée | Valeur remplacée par une autre valeur. |

---

## 8. Duplication d'article

Le configurateur doit permettre de dupliquer un article existant afin de créer rapidement une référence proche, tout en imposant un nouveau code article et une traçabilité vers l'article source.

| Élément | Copie recommandée | Commentaire |
|---|---|---|
| Catégorie, famille, type, unité | Oui | Base de l'article source conservée. |
| Attributs | Oui, modifiables | Les attributs doivent être nettoyés et normalisés après modification. |
| Code article | Non | Nouveau code obligatoire : généré ou saisi. |
| Désignation | Non | Régénérée ou modifiée. |
| Nomenclature | Optionnel | Copier telle quelle ou copier puis recalculer selon les attributs. |
| Gamme | Optionnel | Copier ou adapter selon famille, taille, matière, client. |
| Documents, fournisseurs, coûts | Optionnel | À contrôler selon droits et règles métier. |

**Données transactionnelles à ne jamais copier**

- Stock réel, réservations, OF, commandes achat, commandes client, mouvements de stock, événements terrain, pegging existant.
- Le pegging et les besoins doivent être recalculés pour le nouvel article.

---

## 9. Exemple métier : vignette de composition

La vignette de composition est un composant configurable généré à partir d'une commande client, d'un article produit fini, d'une sélection de tailles, d'une sélection de couleurs, de la composition matière et d'un code entretien.

| Variable | Type | Mode |
|---|---|---|
| Commande client | Sélection simple | Liste existante uniquement |
| Code article produit fini | Sélection simple | Articles liés à la commande |
| Taille | Sélection multiple | Liste + ajout possible |
| Couleur | Sélection multiple | Liste + ajout possible |
| Composition matière | Texte ou sélection | Ajout possible avec validation |
| Code entretien | Sélection simple | Liste contrôlée |
| Langue | Sélection simple ou multiple | Selon matrice client |

**Règle de génération standard :**

> Nombre de vignettes = tailles sélectionnées × couleurs sélectionnées

Si la langue est génératrice pour un client, la règle devient :

> Nombre de vignettes = tailles sélectionnées × couleurs sélectionnées × langues sélectionnées

| Entrée | Exemple |
|---|---|
| Commande | BC0001 |
| Article produit fini | PULL123 |
| Tailles | S, M, XXL |
| Couleurs | Bleu marine, Vert sauge |
| Composition | 70% laine / 30% acrylique |
| Code entretien | LAV30-SECHNON |

Résultat : 3 tailles × 2 couleurs = 6 vignettes générées.

**Exemples de codes générés**

- VC-BC0001-PULL123-S-BLEU-MARINE
- VC-BC0001-PULL123-M-BLEU-MARINE
- VC-BC0001-PULL123-XXL-VERT-SAUGE

---

## 10. Objets conceptuels retenus

| Objet | Rôle |
|---|---|
| Article | Référence article réelle utilisée par Axioplan. |
| ArticleCategory | Catégorie large de classement. |
| ArticleFamily | Famille métier qui porte les règles de configuration. |
| AttributeDefinition | Définition globale d'un attribut réutilisable. |
| ArticleFamilyAttribute | Usage par défaut d'un attribut dans une famille. |
| CustomerArticleFamilyConfiguration | Matrice client pour une famille article. |
| CustomerArticleFamilyAttribute | Règle client détaillée par attribut. |
| ConfigurationSession | Session de création configurée. |
| ConfigurationInput | Valeur saisie, formatée, normalisée et liée à une option si besoin. |
| AttributeOption | Valeur référentielle d'un attribut. |
| AttributeOptionAlias | Alias client ou langue pour une option. |
| AttributeFormattingRule | Règles de nettoyage, formatage, normalisation et codification. |
| ArticleDuplicationSession | Session de duplication article. |
| ArticleDuplicationChange | Traçabilité des attributs modifiés lors d'une duplication. |

---

## 11. Règles de gestion à valider

1. Un article peut être produit fini, semi-fini ou composant.
2. Les attributs sont globaux et réutilisables dans plusieurs familles article.
3. La matrice d'utilisation des attributs est spécifique au client et à la famille article.
4. Toute valeur saisie doit être nettoyée, formatée et normalisée avant usage.
5. La création de nouvelles valeurs est gouvernée par statut et contrôle anti-doublon.
6. Le code article généré ou saisi doit être unique et conforme aux règles de codification.
7. La duplication impose toujours un nouveau code article.
8. Les données transactionnelles ne sont jamais copiées lors d'une duplication.
9. Les articles générés ou dupliqués conservent une traçabilité vers leur session ou article source.
10. Les règles de génération peuvent varier selon le client, la famille, la saison ou la commande.

---

## 12. Prochaines étapes

| Étape | Objectif |
|---|---|
| 1. Validation métier du cadrage article | Confirmer les règles de création, attributs, matrices client et duplication. |
| 2. Cadrage nomenclature | Définir BOM multi-niveau, explosion, aplatissement et liens avec articles configurés. |
| 3. Cadrage pegging | Relier besoins composants, demandes, lignes, OF, opérations, stocks et achats. |
| 4. Découpage MVP | Prioriser les familles : vignette composition, carteline, étiquette taille, packaging. |
| 5. Rédaction des prompts Codex | Transformer le cadrage validé en lots de développement ciblés et testables. |

**Synthèse finale**

- Le configurateur article Axioplan doit rendre le référentiel article paramétrable et robuste.
- La matrice client est le cœur du dispositif : elle pilote les attributs, les règles de génération, la codification, la désignation et les liens avec nomenclature et pegging.
- Ce cadrage prépare le passage au configurateur de nomenclature multi-niveau, à l'affichage aplati et au pegging.
