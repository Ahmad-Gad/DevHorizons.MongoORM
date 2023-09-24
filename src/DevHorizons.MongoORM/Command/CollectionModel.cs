namespace DevHorizons.MongoORM.Command
{
    public class CollectionModel : ICollectionModel
    {
        public  string DatabaseName { get; set; }

        public string CollectionName { get; set; }
    }
}
