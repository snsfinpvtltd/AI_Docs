using System.Diagnostics.CodeAnalysis;

namespace ClinicalAgent.Core;

/// <summary>Discriminated union for operation results. Use instead of throwing for expected failures.</summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly string? _error;

    private Result(T value)      { _value = value; IsSuccess = true; }
    private Result(string error) { _error = error; IsSuccess = false; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary>The success value. Throws if the result is a failure.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result is a failure: {_error}");

    /// <summary>The error message. Throws if the result is a success.</summary>
    public string Error => !IsSuccess ? _error! : throw new InvalidOperationException("Result is a success.");

    public static Result<T> Ok(T value)        => new(value);
    public static Result<T> Fail(string error) => new(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure)
        => IsSuccess ? onSuccess(Value) : onFailure(Error);
}
