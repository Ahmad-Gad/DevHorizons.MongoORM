namespace DevHorizons.MongoORM.Provisioning.Index
{

    using Settings;
    public class MongoIndex
    {
        public string Name { get; set; }

        public ICollection<IndexField> Fields { get; set; }

        public MongoCollationSettings CollationOptions { get; set; }

        public bool? Unique { get; set; }

        public bool DropExisting { get; set; }
    }
}
