namespace DevHorizons.MongoORM.Result
{
    using Logging;

    public interface IResultBase
    {
        bool Success { get; set; }

        CommandStatus CommandStatus { get; set; }

        long MatchedCount { get; set; }

        long AffectedCount { get; set; }

        ICollection<ILogDetails> Errors { get; set; }
    }
}
