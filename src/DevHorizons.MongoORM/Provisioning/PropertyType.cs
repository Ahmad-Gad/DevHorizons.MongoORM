namespace DevHorizons.MongoORM.Provisioning.Validation
{
    using System.ComponentModel;

    using MongoDB.Bson;

    public enum PropertyType
    {
        [Description("double")]
        Double = BsonType.Double,

        [Description("string")]
        String = BsonType.String,

        [Description("object")]
        Document = BsonType.Document,

        [Description("array")]
        Array = BsonType.Array,

        [Description("binData")]
        Binary = BsonType.Binary,

        [Description("objectId")]
        ObjectId = BsonType.ObjectId,

        [Description("bool")]
        Boolean = BsonType.Boolean,

        [Description("date")]
        DateTime = BsonType.DateTime,

        [Description("null")]
        Null = BsonType.Null,

        [Description("regex")]
        RegularExpression = BsonType.RegularExpression,

        [Description("javascript")]
        JavaScript = BsonType.JavaScript,

        [Description("int")]
        Int32 = BsonType.Int32,

        [Description("timestamp")]
        Timestamp = BsonType.Timestamp,

        [Description("long")]
        Int64 = BsonType.Int64,

        [Description("decimal")]
        Decimal = BsonType.Decimal128,

        [Description("maxKey")]
        MaxKey = BsonType.MaxKey,

        [Description("minKey")]
        MinKey = BsonType.MinKey
    }
}
