using System;
namespace Lumora.Core
{
    public record ResultEnum<Tin, TError> where TError : System.Enum
    {
        public ResultEnum(Tin value){
            this.Value = value;
            this.IsSuccess = true;
        }
        public ResultEnum(TError error){
            this.TypedError = error;
            this.IsSuccess = false;
        }
        public bool IsSuccess { get; }
        public TError? TypedError { get; }
        public Tin? Value { get; }
        public static implicit operator ResultEnum<Tin, TError>(Tin value) => new(value);
        public static implicit operator ResultEnum<Tin, TError>(TError error) => new(error);
    }
}
