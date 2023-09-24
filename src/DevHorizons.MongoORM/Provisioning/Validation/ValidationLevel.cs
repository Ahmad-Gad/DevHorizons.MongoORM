namespace DevHorizons.MongoORM.Provisioning.Validation
{
    using System.ComponentModel;

    using MongoDB.Driver;

    [DefaultValue(Strict)]
    public enum ValidationLevel
    {
        /// <summary>
        ///    Strict document validation.
        /// </summary>
        Strict = DocumentValidationLevel.Strict,

        /// <summary>
        ///    Moderate document validation.
        /// </summary>
        Moderate = DocumentValidationLevel.Moderate,

        /// <summary>
        ///   No document validation.
        /// </summary>
        Off = DocumentValidationLevel.Off
    }
}
