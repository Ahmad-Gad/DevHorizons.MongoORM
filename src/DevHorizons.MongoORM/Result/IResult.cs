namespace DevHorizons.MongoORM.Result
{
    public interface IResult<T> : IResultBase
    {
#nullable enable
        T? Value { get; set; }
#nullable disable
    }
}
