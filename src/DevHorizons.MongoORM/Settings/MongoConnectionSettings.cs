namespace DevHorizons.MongoORM.Settings
{
    //https://mongodb.github.io/mongo-csharp-driver/2.17/reference/driver/connecting/#re-use
    public class MongoConnectionSettings : IMongoConnectionSettings
    {
        public string ConnectionString { get; set; }

        public string Database { get; set; }

        public MongoCollationSettings CollationSettings { get; set; } = new MongoCollationSettings();
    }
}
