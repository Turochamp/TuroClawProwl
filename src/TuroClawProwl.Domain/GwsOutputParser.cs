namespace TuroClawProwl.Domain;

// gws prints a keyring-backend banner, and sometimes warnings, before its JSON,
// so the payload is located by balancing brackets from the first structural
// character rather than by assuming stdout starts with one.
public static class GwsOutputParser
{
    public static string? ExtractJson(string stdout)
    {
        ArgumentNullException.ThrowIfNull(stdout);

        var start = stdout.IndexOfAny(['{', '[']);
        if (start < 0) return null;

        var opener = stdout[start];
        var closer = opener == '{' ? '}' : ']';

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < stdout.Length; i++)
        {
            var c = stdout[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == opener) depth++;
            else if (c == closer)
            {
                depth--;
                if (depth == 0) return stdout[start..(i + 1)];
            }
        }

        return null;
    }
}
