namespace BuildingBlocks.Application.Results;

public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorKind.None);
}

public enum ErrorKind
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
    Failure = 6,
    ServiceUnavailable = 7,
    Cancelled = 8
}
