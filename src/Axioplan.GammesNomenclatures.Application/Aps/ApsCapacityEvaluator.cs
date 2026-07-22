using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Capacity;

namespace Axioplan.GammesNomenclatures.Application.Aps;

/// <summary>
/// Evaluation partagee Cap_brute → Cap_cum pour capacites APS (referentiel + reservations).
/// </summary>
public sealed class ApsCapacityEvaluator(
    IApsCapacityRepository capacityRepository,
    IApsReferentialRepository referentialRepository)
{
    public async Task<ApsResourceCapacitySummaryDto> EvaluateResourceAsync(
        ApsCapacityResourceDto resource,
        DateOnly from,
        DateOnly to,
        string? familyCode = null,
        string? teamCode = null,
        CancellationToken cancellationToken = default)
    {
        var schedule = await referentialRepository.GetResourceScheduleAsync(resource.Code, cancellationToken);
        var unavails = await capacityRepository.GetUnavailabilitiesAsync(resource.Code, from, to, cancellationToken);
        var effectiveTeam = teamCode ?? schedule.DefaultTeamCode ?? "JOUR";
        var eta = await capacityRepository.GetEtaAsync(resource.Code, familyCode, effectiveTeam, cancellationToken);
        var reservations = await capacityRepository.GetReservationsAsync(resource.Code, from, to, cancellationToken);
        var tiers = await capacityRepository.GetTiersAsync(resource.Code, cancellationToken);

        var buckets = new List<ApsNetCapacityResult>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var isOpen = IsBucketOpen(resource.Code, d, schedule);
            var duration = ResolveDurationHours(resource, schedule);
            var reserved = reservations.Where(r => r.BucketDate == d).Sum(r => r.Quantity);
            buckets.Add(NetCapacityEngine.ComputeBucket(
                resource.Code,
                resource.ResourceType,
                resource.Unit,
                new ApsCapacityBucket(d, effectiveTeam, duration, isOpen),
                resource.ResourceCount,
                unavails,
                eta,
                resource.RhoTarget,
                reserved,
                resource.ConfirmationStatus));
        }

        var capCum = NetCapacityEngine.ComputeCapCum(
            resource.Code,
            resource.Unit,
            from,
            to,
            DateOnly.FromDateTime(DateTime.Today),
            buckets,
            tiers);

        return new ApsResourceCapacitySummaryDto(
            resource.Code,
            resource.Label,
            resource.ResourceType,
            resource.Unit,
            from,
            to,
            capCum.CapCumEngageable,
            capCum.CapCumWithActiveTiers,
            buckets,
            capCum.Tiers);
    }

    public async Task<double> EvaluateCapCumEngageableAsync(
        string resourceCode,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var resource = await capacityRepository.GetResourceAsync(resourceCode, cancellationToken);
        if (resource is null)
        {
            return 0;
        }

        var summary = await EvaluateResourceAsync(resource, from, to, cancellationToken: cancellationToken);
        return summary.CapCumEngageable;
    }

    public async Task<double?> GetBathMaxFillAsync(string resourceCode, CancellationToken cancellationToken = default)
        => await capacityRepository.GetBathMaxFillAsync(resourceCode, cancellationToken);

    private static bool IsBucketOpen(string resourceCode, DateOnly date, ApsResourceScheduleDto schedule)
    {
        if (schedule.RegimeCode.Equals("REG_6x24", StringComparison.OrdinalIgnoreCase)
            || resourceCode.Contains("TRICOT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return date.DayOfWeek is not DayOfWeek.Sunday;
    }

    private static double ResolveDurationHours(ApsCapacityResourceDto resource, ApsResourceScheduleDto schedule)
    {
        if (resource.ResourceType == "LOT")
        {
            return 0;
        }

        if (schedule.TeamDurationHours > 0)
        {
            return schedule.TeamDurationHours;
        }

        if (schedule.RegimeCode.Equals("REG_6x24", StringComparison.OrdinalIgnoreCase))
        {
            return resource.DefaultDurationHours > 0 ? resource.DefaultDurationHours : 24;
        }

        return resource.DefaultDurationHours > 0 ? resource.DefaultDurationHours : 8;
    }
}
