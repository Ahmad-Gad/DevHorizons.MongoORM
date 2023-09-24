namespace DevHorizons.MongoORM.Logging
{
    public enum LogSource
    {
        Unspecified = 0,

        Connection = 1,

        Command = 2,

        Validation = 4,

        OtherException = 8,
    }
}
