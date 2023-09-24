namespace DevHorizons.MongoORM.Provisioning
{
    using Index;

    using MongoDB.Driver;

    using Settings;

    using Validation;

    public class Collection
    {
        public string DatabaseName { get; set; }

        public string CollectionName { get; set; }

        public MongoCollationSettings Collation { get; set; }

        public ICollection<MongoIndex> Indexes { get; set; }

        public bool DropExistingIndexes { get; set; }

        public SchemaValidation SchemaValidation { get; set; }

        internal IMongoCollection<dynamic> MongoCollection { get; set; }

        internal bool Exists { get; set; }
    }
}