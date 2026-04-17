namespace BuildingBlocks.Application.Results;

public sealed class Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error)
    {
        if (error == Error.None)
        {
            throw new ArgumentException("A failure result requires a concrete error.", nameof(error));
        }

        return new(false, error);
    }
}

public sealed class Result<TValue>
{
    private Result(bool isSuccess, TValue? value, Error error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public TValue? Value { get; }

    public Error Error { get; }

    public static Result<TValue> Success(TValue value) => new(true, value, Error.None);

    public static Result<TValue> Failure(Error error)
    {
        if (error == Error.None)
        {
            throw new ArgumentException("A failure result requires a concrete error.", nameof(error));
        }

        return new(false, default, error);
    }
}
