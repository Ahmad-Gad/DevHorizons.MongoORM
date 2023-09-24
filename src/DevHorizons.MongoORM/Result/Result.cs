namespace DevHorizons.MongoORM.Result
{
    using Logging;

    public class Result<T> : IResult<T>
    {
        public bool Success { get; set; }

        public CommandStatus CommandStatus { get; set; }

        public long MatchedCount { get; set; }

        public long AffectedCount { get; set; }

#nullable enable
        public T? Value { get; set; }
#nullable disable

        public ICollection<ILogDetails> Errors { get; set; }
    }
}
