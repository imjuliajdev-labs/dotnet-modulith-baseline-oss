namespace BuildingBlocks.Application;

public sealed record CursorPagedResult<T>(
    IReadOnlyCollection<T> Items,
    string? NextCursor)
{
    public static CursorPagedResult<T> Empty => new(Array.Empty<T>(), null);
}

public enum CursorDecodeStatus
{
    None = 0,
    Invalid = 1,
    Valid = 2,
}

public readonly record struct CursorDecodeResult(
    CursorDecodeStatus Status,
    string? SortValue,
    string? Id)
{
    public static CursorDecodeResult None { get; } = new(CursorDecodeStatus.None, null, null);

    public static CursorDecodeResult Invalid { get; } = new(CursorDecodeStatus.Invalid, null, null);

    public static CursorDecodeResult Valid(string sortValue, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sortValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new CursorDecodeResult(CursorDecodeStatus.Valid, sortValue, id);
    }
}

public static class CursorEncoding
{
    public static string Encode(string sortValue, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sortValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var payload = $"{sortValue}|{id}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
    }

    public static CursorDecodeResult Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return CursorDecodeResult.None;
        }

        try
        {
            var payload = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var separatorIndex = payload.IndexOf('|');
            if (separatorIndex <= 0 || separatorIndex >= payload.Length - 1)
            {
                return CursorDecodeResult.Invalid;
            }

            return CursorDecodeResult.Valid(payload[..separatorIndex], payload[(separatorIndex + 1)..]);
        }
        catch
        {
            return CursorDecodeResult.Invalid;
        }
    }
}
