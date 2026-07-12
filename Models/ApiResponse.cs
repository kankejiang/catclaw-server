using System.Text.Json.Serialization;

namespace CatClawMusicServer.Models;

/// <summary>CatClaw API v1 统一响应信封</summary>
public class ApiResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "ok";

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Code = 0, Message = "ok", Data = data };

    public static ApiResponse<T> Error(int code, string message) => new() { Code = code, Message = message };
}

/// <summary>分页响应</summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
}

/// <summary>错误码常量</summary>
public static class ErrorCodes
{
    // 认证错误 1xxx
    public const int Unauthorized = 1001;
    public const int TokenExpired = 1002;
    public const int InvalidCredentials = 1003;
    public const int Forbidden = 1004;

    // 参数错误 2xxx
    public const int InvalidParameter = 2001;
    public const int DuplicateEntry = 2002;

    // 资源错误 3xxx
    public const int NotFound = 3001;
    public const int FileNotFound = 3002;

    // 服务端错误 4xxx
    public const int InternalError = 4001;
    public const int TranscodeError = 4002;
}
