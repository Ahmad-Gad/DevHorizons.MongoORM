namespace DevHorizons.MongoORM.Result.CommandListCollectionInfo
{
    using System.Text.Json.Serialization;

    public class CollectionInfoOptions
    {
        [JsonPropertyName("validator")]
        public dynamic Validator { get; set; }

        [JsonPropertyName("validationLevel")]
        public string ValidationLevel { get; set; }

        [JsonPropertyName("validationAction")]
        public string ValidationAction { get; set; }
    }
}
