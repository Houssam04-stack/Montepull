using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Simulation;

/// <summary>
/// Gamme pantalon inspiree du fichier prod (structure 10-300, centres WU-*).
/// Les operations qui consomment un composant reprennent sa quantite BOM taille M.
/// </summary>
internal static class PantalonGammeTemplate
{
    private sealed record OpTemplate(int Number, string Name, string WorkCenter, string? ComponentCode, double TimeMinutes);

    private static readonly OpTemplate[] Operations =
    [
        new(10, "Coupe panneaux tissu (TISSU_BASE)", "WU-PMA25", "TISSU_BASE", 15.0),
        new(20, "Preparation fil (FIL)", "WU-PMA25", "FIL", 8.0),
        new(30, "Bordage panneaux", "WU-PO21", null, 24.0),
        new(40, "Lavage", "WU-PO21", null, 21.0),
        new(50, "Sechoire", "WU-PO21", null, 18.0),
        new(60, "Controle panneaux", "WU-PO21", null, 15.0),
        new(70, "Calandrage", "WU-PMA33", null, 15.0),
        new(80, "Matlassage", "WU-PMA16", null, 60.0),
        new(90, "Coupe scie", "WU-PMA16", null, 18.0),
        new(100, "Compositage assemblage", "WU-PMA01", null, 15.0),
        new(110, "Montage ceinture", "WU-PMA01", null, 18.0),
        new(120, "Montage poche", "WU-PMA01", null, 45.0),
        new(130, "Couture cote (FIL)", "WU-PMA01", "FIL", 60.0),
        new(140, "Pose boutons (BOUTON)", "WU-PMA01", "BOUTON", 30.0),
        new(150, "Bordage encolure", "WU-PMA01", null, 18.0),
        new(160, "Montage zip (ZIP)", "WU-PMA01", "ZIP", 90.0),
        new(170, "Separation et demaillage bande", "WU-PMA01", null, 30.0),
        new(180, "Fermeture epaule", "WU-PMA01", null, 15.0),
        new(190, "Point d'arret bas cote", "WU-PMA01", null, 9.0),
        new(200, "Point d'arret bas jambe", "WU-PMA01", null, 9.0),
        new(210, "Point d'arret ceinture", "WU-PMA01", null, 6.0),
        new(220, "Fixation griffe de marque", "WU-PMA01", null, 15.0),
        new(230, "Fixation etiquette composition", "WU-PMA01", null, 9.0),
        new(240, "Finition", "WU-PMA01", null, 60.0),
        new(250, "Controle", "WU-PMA01", null, 60.0),
        new(260, "Repassage", "WU-PMA38", null, 60.0),
        new(270, "Controle mesure", "WU-PMA41", null, 24.0),
        new(280, "Controle final", "WU-PMA41", null, 24.0),
        new(290, "Etiquetage", "WU-PMA41", null, 18.0),
        new(300, "Mise en sachet (EMBALLAGE)", "WU-PMA41", "EMBALLAGE", 15.0),
    ];

    public static IReadOnlyList<RoutingOperationDetailRow> Build(
        IReadOnlyDictionary<string, double> componentQuantitiesPerUnit)
    {
        return Operations.Select(op =>
        {
            var qty = 1.0;
            if (!string.IsNullOrWhiteSpace(op.ComponentCode)
                && componentQuantitiesPerUnit.TryGetValue(op.ComponentCode, out var componentQty))
            {
                qty = componentQty;
            }

            return new RoutingOperationDetailRow(
                op.Number,
                op.Name,
                op.WorkCenter,
                qty,
                op.TimeMinutes);
        }).ToList();
    }
}
