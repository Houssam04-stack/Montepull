using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Simulation;

public static class DuplicationOptionsDefaults
{
    public static IReadOnlyList<DuplicationSizeOptionDto> Sizes { get; } =
    [
        new("S", 0.90, true, 0),
        new("M", 1.00, true, 1),
        new("L", 1.15, true, 2),
        new("XL", 1.30, true, 3),
    ];

    public static IReadOnlyList<DuplicationColorOptionDto> Colors { get; } =
    [
        new("Noir", 1.0, true, 0),
        new("Bleu", 1.0, true, 1),
    ];
}
