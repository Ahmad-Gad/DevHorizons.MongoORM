namespace DevHorizons.MongoORM.Result.CommandListCollectionInfo
{
    using System.Text.Json.Serialization;

    public class CollectionInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("options")]
        public CollectionInfoOptions Options { get; set; }

        [JsonIgnore]
        public bool HasValidator
        {
            get
            {
                return this.Options?.Validator is not null;
            }
        }
    }
}
