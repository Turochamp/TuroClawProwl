using System.Diagnostics.CodeAnalysis;
using Serilog.Core;
using Serilog.Events;

namespace TuroClawProwl.Infrastructure.Logging;

// Serilog destructuring policy that masks the value of any object property
// whose name matches /token|password|secret/i (case-insensitive). Closes
// QR-S2 mechanically: even if a future log call accidentally includes a
// sensitive property on a destructured object, the sink receives the mask.
public sealed class SensitivePropertyMaskingPolicy : IDestructuringPolicy
{
    private const string Mask = "***";

    private static readonly string[] SensitiveNames =
    {
        "token", "password", "secret", "apikey", "authorization",
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        var type = value.GetType();
        if (type.IsPrimitive || value is string || type.IsEnum)
        {
            result = null;
            return false;
        }

        var hasSensitive = false;
        var properties = new List<LogEventProperty>();
        foreach (var prop in type.GetProperties())
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            object? rawValue;
            try { rawValue = prop.GetValue(value); }
            catch { continue; }

            if (IsSensitive(prop.Name))
            {
                hasSensitive = true;
                properties.Add(new LogEventProperty(prop.Name, new ScalarValue(Mask)));
            }
            else
            {
                properties.Add(new LogEventProperty(prop.Name, propertyValueFactory.CreatePropertyValue(rawValue, destructureObjects: true)));
            }
        }

        if (!hasSensitive)
        {
            result = null;
            return false;
        }

        result = new StructureValue(properties);
        return true;
    }

    private static bool IsSensitive(string propertyName)
    {
        foreach (var needle in SensitiveNames)
        {
            if (propertyName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
