namespace DevHorizons.MongoORM.Command
{
    public interface ICollectionModel
    {
        string DatabaseName { get; set; }

        string CollectionName { get; set; }
    }
}
