namespace DevHorizons.MongoORM.Settings
{
    public class MongoCollationSettings
    {
        public MongoLocale Locale { get; set; } = MongoLocale.English;

        public MongoCollationStrength CollationStrength { get; set; }

        public MongoCollationCaseFirst CollationCaseFirst { get; set; }

        /// <summary>
        /// Gets whether the collation is case sensitive at strength 1 and 2.
        /// </summary>
        public bool CaseLevel { get; set; }
    }
}
