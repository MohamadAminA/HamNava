namespace Hamnava.Core.Common;

public class ServiceResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static ServiceResult Ok(string message = "عملیات با موفقیت انجام شد.") =>
        new() { Succeeded = true, Message = message };

    public static ServiceResult Fail(string message, string? errorCode = null, IReadOnlyList<string>? errors = null) =>
        new() { Succeeded = false, Message = message, ErrorCode = errorCode, Errors = errors ?? [message] };
}

public sealed class ServiceResult<T> : ServiceResult
{
    public T? Data { get; init; }

    public static ServiceResult<T> Ok(T data, string message = "عملیات با موفقیت انجام شد.") =>
        new() { Succeeded = true, Data = data, Message = message };

    public new static ServiceResult<T> Fail(string message, string? errorCode = null, IReadOnlyList<string>? errors = null) =>
        new() { Succeeded = false, Message = message, ErrorCode = errorCode, Errors = errors ?? [message] };
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
