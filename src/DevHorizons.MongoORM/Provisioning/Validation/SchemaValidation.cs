namespace DevHorizons.MongoORM.Provisioning.Validation
{
    public class SchemaValidation
    {
        /// <summary>
        ///    The JSON schema validation for the collection.
        ///    <para><see href="https://www.mongodb.com/docs/manual/core/schema-validation/"/></para>
        /// </summary>
        public string JsonSchemaContents { get; set; }

        public string JsonSchemaFile { get; set; }

        public ValidationLevel ValidationLevel { get; set; }

        /// <summary>
        ///    If set to <c>true</c>, it will just remove the current validation and it will omit/ignore the both properties "<see cref="JsonSchemaContents"/>" and "<see cref="JsonSchemaFile"/>" even if they are specified.
        /// </summary>
        public bool ClearValidation { get; set; }

        public bool OverrideValidation { get; set; }
    }
}
