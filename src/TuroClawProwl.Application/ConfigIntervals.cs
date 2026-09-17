namespace TuroClawProwl.Application;

public static class ConfigIntervals
{
    public static readonly TimeSpan MinimumSnapshotInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinimumHeartbeatInterval = TimeSpan.FromMinutes(15);

    public static TimeSpan EffectiveSnapshotInterval(TuroClawProwlConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.SnapshotInterval < MinimumSnapshotInterval
            ? MinimumSnapshotInterval
            : config.SnapshotInterval;
    }

    // A heartbeat shorter than the refresh would republish identical timestamps
    // for no reason, so the snapshot interval is also a floor.
    public static TimeSpan EffectiveHeartbeatInterval(TuroClawProwlConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var floor = EffectiveSnapshotInterval(config);
        if (floor < MinimumHeartbeatInterval) floor = MinimumHeartbeatInterval;

        return config.HeartbeatInterval < floor ? floor : config.HeartbeatInterval;
    }
}
