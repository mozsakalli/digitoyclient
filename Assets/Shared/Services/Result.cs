// core.upm Helpers/Result.cs — birebir (Unity bağımlılığı yoktu).
using System;
using M;

namespace Digitoy
{
    [Serializable]
    public class Result
    {
        public bool Ok => Error is null or ErrorCode.None;
        public ErrorCode? Error;
        public static Result Success() => new();
        public static Result Failure(ErrorCode? errorCode, object details = null)
        {
            return new Result
            {
                Error = errorCode ?? ErrorCode.UnknownError
            };
        }
    }

    [Serializable]
    public class Result<T>
    {
        public bool Ok => Error is null or ErrorCode.None;
        public ErrorCode? Error { get; private set; } = ErrorCode.None;
        public T Data { get; private set; }

        private Result() { }

        public static Result<T> Success(T data)
        {
            return new Result<T>
            {
                Data = data
            };
        }

        public static Result<T> Failure(ErrorCode? error, object details = null)
        {
            return new Result<T>
            {
                Error = error ?? ErrorCode.UnknownError
            };
        }
    }
}
