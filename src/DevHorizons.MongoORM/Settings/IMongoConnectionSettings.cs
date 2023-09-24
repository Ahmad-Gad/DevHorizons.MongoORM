namespace DevHorizons.MongoORM.Settings
{
    //https://mongodb.github.io/mongo-csharp-driver/2.17/reference/driver/connecting/#re-use
    public interface IMongoConnectionSettings
    {
        string ConnectionString { get; set; }

        string Database { get; set; }

        MongoCollationSettings CollationSettings { get; set; }
    }
}
