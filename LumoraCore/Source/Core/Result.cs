namespace Lumora.Core
{
    public record Result<Tin, TError> where TError : System.Enum
    {
        public Result(Tin value) { Value = Value; }
        public Result(TError error) { TypedError = error; }
        public TError? TypedError { get; private set; }
        public Tin? Value { get; }
    }
}
