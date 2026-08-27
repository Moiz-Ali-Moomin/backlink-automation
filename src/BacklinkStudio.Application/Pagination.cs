using System.Text;

namespace BacklinkStudio.Application;

public sealed record PageRequest(int Limit = 50, string? Cursor = null)
{
    public int BoundedLimit => Math.Clamp(Limit, 1, 100);
}

public sealed record PageResult<T>(IReadOnlyList<T> Items, string? NextCursor);

public readonly record struct PageCursor(DateTimeOffset CreatedAt, Guid Id);

public static class CursorCodec
{
    public static string Encode(PageCursor cursor)
    {
        var value = $"{cursor.CreatedAt.ToUnixTimeMilliseconds()}:{cursor.Id:D}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static PageCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(normalized)).Split(':', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], out var milliseconds) || !Guid.TryParse(parts[1], out var id))
            {
                throw new ValidationException("Cursor is invalid.");
            }

            return new PageCursor(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds), id);
        }
        catch (FormatException)
        {
            throw new ValidationException("Cursor is invalid.");
        }
    }
}
