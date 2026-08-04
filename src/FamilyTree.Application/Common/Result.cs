namespace FamilyTree.Application.Common;

/// <summary>
/// Outcome of an application service call. Business rules are expected
/// conditions rather than exceptional ones — a duplicate name or a circular
/// relationship is something the user needs told, not a stack trace — so
/// services return this instead of throwing.
/// </summary>
/// <remarks>
/// A warning is a success: the operation completed and the value is present,
/// but something is worth surfacing. Duplicate people are the motivating case,
/// since two cousins genuinely can share a name and a birth year, and blocking
/// that would make the app wrong about real families.
/// </remarks>
public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, string? error, string? warning)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        Warning = warning;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? Error { get; }

    public string? Warning { get; }

    public bool IsWarning => Warning is not null;

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> SuccessWithWarning(T value, string warning) =>
        new(true, value, null, warning);

    public static Result<T> Failure(string error) => new(false, default, error, null);
}

/// <summary>Non-generic companion for operations that return no value.</summary>
public sealed class Result
{
    private Result(bool isSuccess, string? error, string? warning)
    {
        IsSuccess = isSuccess;
        Error = error;
        Warning = warning;
    }

    public bool IsSuccess { get; }

    public string? Error { get; }

    public string? Warning { get; }

    public bool IsWarning => Warning is not null;

    public static Result Success() => new(true, null, null);

    public static Result SuccessWithWarning(string warning) => new(true, null, warning);

    public static Result Failure(string error) => new(false, error, null);
}
