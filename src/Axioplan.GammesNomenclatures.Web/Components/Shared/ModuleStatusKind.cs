namespace Axioplan.GammesNomenclatures.Web.Components.Shared;

public enum ModuleStatusKind
{
    Operational,
    Partial,
    Experimental,
    MissingData
}

public static class ModuleStatusLabels
{
    public static string Label(ModuleStatusKind kind) => kind switch
    {
        ModuleStatusKind.Operational => "Opérationnel",
        ModuleStatusKind.Partial => "Partiel",
        ModuleStatusKind.Experimental => "Expérimental",
        ModuleStatusKind.MissingData => "Données manquantes",
        _ => kind.ToString()
    };

    public static string CssClass(ModuleStatusKind kind) => kind switch
    {
        ModuleStatusKind.Operational => "status-operational",
        ModuleStatusKind.Partial => "status-partial",
        ModuleStatusKind.Experimental => "status-experimental",
        ModuleStatusKind.MissingData => "status-missing",
        _ => "status-partial"
    };
}
