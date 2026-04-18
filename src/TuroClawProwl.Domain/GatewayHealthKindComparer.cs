namespace TuroClawProwl.Domain;

public sealed class GatewayHealthKindComparer : IEqualityComparer<GatewayHealth>
{
    public static readonly GatewayHealthKindComparer Instance = new();

    private GatewayHealthKindComparer() { }

    public bool Equals(GatewayHealth? x, GatewayHealth? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.GetType() == y.GetType();
    }

    public int GetHashCode(GatewayHealth obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.GetType().GetHashCode();
    }
}
