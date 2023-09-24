namespace DevHorizons.MongoORM.Settings
{
    using System.ComponentModel;

    using MongoDB.Driver;
    [DefaultValue(Off)]
    public enum MongoCollationCaseFirst
    {
        Off = CollationCaseFirst.Off,

        Upper = CollationCaseFirst.Upper,

        Lower = CollationCaseFirst.Lower
    }
}

