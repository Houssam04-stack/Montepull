# Rapport final — MVP Configurateur Gammes & Nomenclatures + Articles

Date : 2026-07-06

## 1. Ce qui est terminé

| Module | Statut | Description |
|---|---|---|
| Configurateur Gammes & Nomenclatures | Terminé | Simulation taille×couleur, génération, traces |
| Configurateur Article | Terminé | Création, duplication, matrice client, attributs |
| Paramètres | Terminé | Coefficients, pertes BOM, paramètres CBN/Pegging SQL |
| CBN (moteur v1 + avancé) | Terminé | Multi-niveaux PF→SF→composants, paramètres SQL, recalcul marqué |
| Pegging (premier niveau) | Terminé | OV, OF, OA, stock, liens bidirectionnels, versions |
| Validation DOUBLYGILF | Terminé (partiel) | Jeu dérivé prod, écarts documentés |
| Interface Blazor | Terminé | `/`, `/parametres`, `/articles`, `/cbn`, `/pegging`, `/consultation` |
| Tests | Terminé | 32 tests Python + 2 tests xUnit Domain |
| Journalisation | Terminé | Table `application_logs` + ILogger |

## 2. Hors périmètre volontaire

- Connexion Sage réelle
- Intégration Axioplan réelle
- IA Planning
- Pegging complet (règles d'allocation métier, capacité, calendriers)
- API HTTP dédiée (Blazor suffit pour le MVP)
- Zone gelée CBN (BR-CBN-004 non implémentée)

## 3. Fichiers créés / modifiés (synthèse)

### SQL
- `database/schema.sql` — pegging, OA/OF, stock, multi-niveaux, logs
- `database/seed/006_multilevel_bom.sql`
- `database/seed/007_pegging.sql`
- `database/seed/005_parameters.sql` — paramètres Pegging + recalcul CBN

### Domain
- `BomFlattener.cs`, `PeggingEngine.cs`, `CbnEngine.cs` (existant enrichi)

### Application / Infrastructure
- `PeggingService.cs`, `PeggingModels.cs`, `IPeggingRepository.cs`
- `SqlServerPeggingRepository.cs`, `SqlServerConsultationRepository.cs`
- `SqlApplicationLogger.cs`
- `SqlServerCbnRepository.cs` — explosion multi-niveaux
- `SqlServerParameterRepository.cs` — marquage recalcul CBN

### Web
- `Pegging.razor`, `Consultation.razor`
- `Cbn.razor`, `MainLayout.razor`, `app.css`

### Tests
- `tests/test_pegging.py`, `tests/test_multilevel_cbn.py`
- `tests/Axioplan.GammesNomenclatures.Domain.Tests/`

### Documentation
- `docs/doublygilf-validation.md`, `docs/guide-utilisateur.md`, ce rapport

## 4. Commandes de lancement

```powershell
python scripts/init_db.py
dotnet run --project src/Axioplan.GammesNomenclatures.Web
```

URL : http://localhost:5280

### Tests

```powershell
python -m unittest discover -s tests
dotnet test tests/Axioplan.GammesNomenclatures.Domain.Tests
```

## 5. Résultats des tests (dernière exécution)

| Suite | Résultat |
|---|---|
| Python `unittest discover` | **32/32 OK** |
| xUnit Domain | **2/2 OK** |
| `dotnet build` Web | **OK** |

## 6. Parcours de démonstration MVP

1. **Simulation** (`/`) — variantes taille×couleur
2. **Paramètres** (`/parametres`) — coefficients, pertes, règles CBN
3. **CBN** (`/cbn`) — commande `DOUBLYGILF-OV-001`, famille `PULL_COL_ROND`
4. **Pegging** (`/pegging`) — liens OV→besoin→OF/OA/stock
5. **Consultation** (`/consultation`) — gammes prod, BOM, nomenclature aplatie
6. **Articles** (`/articles`) — configurateur article

## 7. Points « À confirmer » avec le métier

| Point | Détail |
|---|---|
| Fichiers DOUBLYGILF réels | Non fournis — seed dérivé de `docs/prod (1).md` |
| Besoin alloué 152,094 | Écart vs 15,789 KG FIL-MINT — unité et BOM prod à confirmer |
| Règles d'allocation Pegging | MVP : min(besoin, offre) — priorité OA/OF/stock à confirmer |
| PEGGING_MAX_CASCADE_LEVEL | Défaut 5 — profondeur réelle à confirmer |
| Statuts BOM DRAFT acceptés | Inclus pour tests locaux |
| BR-CBN-004 zone gelée | Non implémentée |
| Nomenclature prod réelle | BOM exemple simulé (PANNEAU-SF, FIL-MINT, etc.) |
| Unité temps gamme | `TO_CONFIRM` |

Voir aussi `docs/a-confirmer.md` et `docs/doublygilf-validation.md`.
