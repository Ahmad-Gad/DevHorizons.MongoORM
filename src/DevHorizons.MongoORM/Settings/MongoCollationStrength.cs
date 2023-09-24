namespace DevHorizons.MongoORM.Settings
{
    using System.ComponentModel;
    using MongoDB.Driver;

    [DefaultValue(Primary)]
    public enum MongoCollationStrength
    {
        Primary = CollationStrength.Primary,

        Secondary = CollationStrength.Secondary,

        Tertiary = CollationStrength.Tertiary,

        Quaternary = CollationStrength.Quaternary,

        Identical = CollationStrength.Identical
    }
}
