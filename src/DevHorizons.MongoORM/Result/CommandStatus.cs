namespace DevHorizons.MongoORM.Result
{
    using System;

    [Flags]
    public enum CommandStatus
    {
        ServerFailed = -3,

        ClientFailed = -2,

        Terminated = -1,

        Unspecified = 0,

        Executed = 1,

        Completed = 2,

        PartiallyCompleted = 3
    }
}
