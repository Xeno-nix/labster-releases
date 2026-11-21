using CLMS.Domain.Entities.UnitManagement;

namespace CLMS.Application.Monitoring;

/// <summary>Application-specific transitions for a successful process observation, not a policy engine.</summary>
public static class FlaggedApplicationDetection
{
    public static bool IsNewer(DateTime requestedAt, DateTime? previous) =>
        !previous.HasValue || requestedAt.Ticks / 10 > previous.Value.Ticks / 10;

    public static IReadOnlyList<AbnormalActivity> Changes(ApplicationObservation observation,
        IReadOnlyList<MonitoredApplication> registry, IReadOnlyList<AbnormalActivity> active, DateTime? previousObservation = null)
    {
        if (!IsNewer(observation.RequestedAt, previousObservation)
            || observation.RegistryRevision != ApplicationRegistryRevision.Compute(registry)) return [];
        var eligible = registry.Where(a => a.IsEnabled && a.FlagForReview && a.UpdatedAt <= observation.RequestedAt
            && ApplicationProcessNames.AppliesTo(a.Platform, observation.Platform)).ToDictionary(a => a.Id);
        var detected = observation.ApplicationIds.ToHashSet();
        var existing = active.Where(a => a.UnitId == observation.UnitId && a.Type == AbnormalActivityType.FlaggedApplication && !a.EndedAt.HasValue).ToArray();
        var changes = new List<AbnormalActivity>();
        var at = observation.ReceivedAt < observation.RequestedAt ? observation.RequestedAt : observation.ReceivedAt;
        foreach (var app in eligible.Values.Where(a => detected.Contains(a.Id)))
            if (!existing.Any(a => a.ApplicationId == app.Id))
                changes.Add(new(Guid.NewGuid(), observation.UnitId, AbnormalActivityType.FlaggedApplication, app.Id,
                    app.Name, at, null));
        if (observation.IsComplete)
            foreach (var activity in existing)
                if (activity.ApplicationId.HasValue && eligible.ContainsKey(activity.ApplicationId.Value) && !detected.Contains(activity.ApplicationId.Value))
                    changes.Add(activity with { EndedAt = at < activity.DetectedAt ? activity.DetectedAt : at });
        return changes;
    }
}
