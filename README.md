# Montepull - Axioplan MVP

Architecture locale du projet Axioplan pour Montepull.

## Objectif

Construire un MVP professionnel, simple et maintenable, pour remplacer progressivement certaines limites de Sage.

## Feuille de route APS (source : Dossier_APS_conception)

1. **MVP-0** — Référentiel et fiabilisation des données (Gate 0→1) — **parcours principal** `/mvp0`
2. **MVP-1** — Ordonnancement des deux goulots (après GO Gate 0→1)
3. **MVP-2** — Planification niveau 1

Les modules `/aps/*` (journal, CTP, flux, etc.) restent accessibles sous **Prototype / Expérimental** et ne décident pas du GO.

Documentation MVP-0 : [scope](docs/mvp0-scope.md) · [démo](docs/mvp0-demo-guide.md) · [gate](docs/mvp0-gate.md) · [fiabilité](docs/mvp0-reliability-formula.md)

## Ordre de developpement historique (modules existants)

1. Configurateur Gammes & Nomenclatures
2. Configurateur Articles
3. Modules simulation / CBN / APS experimental

## Choix MVP

- Application locale.
- Base SQL Server (instance locale SQL Server Express ou equivalent).
- Modules independants.
- Architecture preparant une connexion future a Sage.
- Direction cible : integration progressive dans Axioplan, application .NET.
- Python reste un support d'analyse, de tests et de demonstration console.

## Structure

```text
docs/                         Documentation officielle projet
database/                     Schema SQL Server et donnees simulees
src/axioplan/core/            Socle technique partage
src/axioplan/modules/         Modules fonctionnels independants
src/Axioplan.GammesNomenclatures.*  Application Blazor .NET
tests/                        Tests automatises
```

## Documentation principale

- [Rapport final MVP](docs/RAPPORT-FINAL-MVP.md)
- [Guide utilisateur](docs/guide-utilisateur.md)
- [Validation DOUBLYGILF](docs/doublygilf-validation.md)
- [Audit avant passage .NET](docs/dotnet-audit-architecture-actuelle.md)
- [Architecture .NET proposee](docs/dotnet-architecture-proposee.md)
- [Domain Model Gammes & Nomenclatures](docs/domain-model-gammes-nomenclatures.md)
- [Use Cases Gammes & Nomenclatures](docs/use-cases-gammes-nomenclatures.md)
- [Business Rules Gammes & Nomenclatures](docs/business-rules-gammes-nomenclatures.md)
- [Sprints de developpement .NET](docs/sprints-developpement-dotnet.md)
- [Modele de donnees](docs/modele-donnees.md)
- [Decoupage modules](docs/decoupage-modules.md)
- [Questions a confirmer](docs/a-confirmer.md)

## Prerequis base de donnees

Le MVP utilise **SQL Server** (base `AxioplanMvp`).

Sur cette machine, **SQL Server Express** (`localhost\SQLEXPRESS`) est disponible. Alternatives possibles :

| Option | Usage | Avantage | Inconvenient |
|--------|-------|----------|--------------|
| **SQL Server Express** (recommande ici) | Dev local Windows | Deja installe, proche de la prod Axioplan | Service Windows a demarrer |
| **LocalDB** | Dev leger Visual Studio | Installation legere | Non installe sur toutes les machines |
| **Docker SQL Server** | Environnement reproductible | Isole, portable | Necessite Docker, plus lourd |

Chaine de connexion par defaut :

```text
Driver={ODBC Driver 18 for SQL Server};Server=localhost\SQLEXPRESS;Database=AxioplanMvp;Trusted_Connection=yes;TrustServerCertificate=yes;
```

Pour l'application .NET (Microsoft.Data.SqlClient) :

```text
Server=localhost\SQLEXPRESS;Database=AxioplanMvp;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;
```

Variables d'environnement optionnelles :

- `AXIOPLAN_SQL_SERVER` : instance SQL Server (defaut `localhost\SQLEXPRESS`)
- `AXIOPLAN_CONNECTION_STRING` : chaine complete pour Python et tests

## Initialiser la base locale

Prerequis : `sqlcmd` (SQL Server tools) et instance SQL Server demarree.

**Premiere installation** (efface toute base existante) :

```powershell
python scripts/init_db.py --reset
```

**Mise a jour sans perdre les donnees** (arguments, formules, articles...) :

```powershell
python scripts/init_db.py
```

Les donnees creees dans Parametres sont stockees dans SQL Server et survivent au redemarrage de l'application. Ne lancez `--reset` que si vous voulez repartir de zero.

Dependances Python pour les tests et la demo console :

```powershell
pip install -r requirements.txt
```

## Lancer l'application Blazor

Le SDK .NET est installe sur `C:\Program Files\dotnet\`, mais si `dotnet` n'est pas reconnu dans le terminal, **fermez et rouvrez le terminal** (ou Cursor) apres l'installation du SDK.

### Option 1 — script (recommande si `dotnet` introuvable)

```powershell
.\scripts\run_web.ps1
```

### Option 2 — commande directe

```powershell
dotnet run --project src/Axioplan.GammesNomenclatures.Web
```

Si `dotnet` n'est pas dans le PATH :

```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project src/Axioplan.GammesNomenclatures.Web
```

Ou pour la session PowerShell en cours :

```powershell
$env:Path += ";C:\Program Files\dotnet"
dotnet run --project src/Axioplan.GammesNomenclatures.Web
```

L'interface est disponible sur **http://localhost:5280** (voir `launchSettings.json`).

### Runtime .NET 8 requis

Le projet cible **net8.0**. Si vous voyez `You must install or update .NET to run this application` avec `Framework: Microsoft.NETCore.App, version 8.0.0`, installez le runtime :

```powershell
winget install Microsoft.DotNet.AspNetCore.8
```

Ou telechargez **ASP.NET Core Runtime 8.0** : https://dotnet.microsoft.com/download/dotnet/8.0

Configuration dans `src/Axioplan.GammesNomenclatures.Web/appsettings.Development.json` :

```json
{
  "Database": {
    "ConnectionString": "Server=localhost\\SQLEXPRESS;Database=AxioplanMvp;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

## Demo console (meme donnees, memes calculs)

```powershell
python scripts/demo_generation.py
```

## Lancer les tests

```powershell
pip install -r requirements.txt
python -m unittest discover -s tests
dotnet test Axioplan.GammesNomenclatures.sln
```

Si `python` n'est pas disponible dans le PATH, utiliser le Python embarque de Codex Desktop.

## Module Imports V1

Nouvelle page Blazor : `/imports`

Fonctionnalites :

- import d'une commande Excel (`.xlsx` / `.xls`) ;
- import d'une nomenclature Excel (`.xlsx` / `.xls`) ;
- choix de la feuille ;
- apercu des donnees ;
- detection automatique des colonnes/lignes utiles ;
- mapping manuel modifiable avant validation ;
- creation automatique des tailles/couleurs manquantes dans SQL Server ;
- creation d'une nouvelle version BOM exploitable par `Simulation`, `Parametres` et `CBN`.

### Test rapide avec les fichiers fournis

1. Ouvrir `/imports`
2. Import commande :
   - choisir `C:\Users\USER\Downloads\DOUBLYGILF.xlsx`
   - verifier la feuille detectee, les lignes metadonnees / tailles / quantites
   - verifier le mapping
   - lancer **Importer la commande**
3. Import nomenclature :
   - choisir `C:\Users\USER\Downloads\Nomenclature DOULBYGILF.xls`
   - verifier la feuille detectee, la ligne d'entetes et le mapping
   - lancer **Importer la nomenclature**
4. Aller sur `/cbn` et lancer un calcul sur la commande importee avec la meme famille produit.

### Hypotheses V1 documentees dans l'application

- la famille produit est selectionnee manuellement avant l'import ;
- pour la commande, la reference importee sert de code article produit fini ;
- pour la nomenclature, la quantite utilise `Besoin` en priorite puis une lecture numerique de `Emploi` ;
- les composants sans famille explicite sont rattaches a la famille technique `IMPORTED_COMPONENT`.

## Pegging V1

Nouvelle visualisation Blazor sur `/pegging` :

- lancement du pegging a partir d'un run CBN ;
- liens bidirectionnels entre besoins et sources ;
- couverture par besoin avec quantites couvertes par `stock`, `OF` et `OA` ;
- reste a couvrir visible pour chaque besoin ;
- vue d'usage des disponibilites (`stock`, `OF`, `OA`) ;
- historique des versions de liens pour audit.

Ordre d'allocation implemente en V1 :

1. stock disponible ;
2. ordres de fabrication ;
3. ordres d'achat.

Points explicitement `A confirmer` :

- politique detaillee d'allocation (FIFO, priorite client, date de besoin) ;
- gestion MTS / MTO / sous-traitance par article ;
- propagation complete des impacts et re-pegging manuel ;
- vue arborescente complete OV → OF PF → OF SF → OA composant quand les OF parent/enfant ne sont pas encore portes par la donnee source.
