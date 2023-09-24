namespace DevHorizons.MongoORM.Connection
{
    using Logging;

    using Provisioning;
    using Provisioning.Validation;

    using MongoDB.Driver;

    using Result;

    using Settings;

    public interface IMongoConnection
    {
        #region Delegates
        /// <summary>
        ///    A delegate to handel the error raised by the class.
        /// </summary>
        /// <param name="error">The raised error.</param>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>08/11/2018 11:13 AM</DateTime>
        /// </Created>
        delegate void RaiseError(ILogDetails error);
        #endregion Delegates

        #region Event Handlers
        event RaiseError ErrorRaised;
        #endregion Event Handlers

        #region Properties
        MongoCollationSettings CollationSettings { get; }

        Guid CorrelationId { get; }

        DateTime StartTime { get; }

        bool Connected { get; }

        string Host { get; }

        ICollection<ILogDetails> Errors { get; }

        IMongoDatabase Database { get; }
        #endregion Properties

        #region Methods
        bool Ping(string databaseName);

        bool Ping(IMongoDatabase mongoDatabase);

        void ClearErrors();

        IMongoDatabase GetDatabase(string databaseName);

        IMongoCollection<T> GetCollection<T>(IMongoDatabase database, string collectionName);

        IMongoCollection<T> GetCollection<T>(string databaseName, string collectionName);

        IResult<T> RunCommand<T>(string jsonCommand, CancellationToken cancellationToken = default);

        bool SetJsonSchemaValidation(string collection, string jsonSchema, ValidationLevel validationLevel, CancellationToken cancellationToken = default);

        bool SetJsonSchemaValidation(string database, string collection, string jsonSchema, ValidationLevel validationLevel, CancellationToken cancellationToken = default);

        bool SetJsonSchemaValidation(IMongoDatabase database, string collection, string jsonSchema, ValidationLevel validationLevel, CancellationToken cancellationToken = default);

        bool DisableValidation(IMongoDatabase database, string collectionName);

        bool DisableValidation(IMongoDatabase database, IMongoCollection<dynamic> collection);

        bool DisableValidation(string databaseName, string collectionName);

        CommandStatus ProvisionCollection(Collection collection);

        CommandStatus ProvisionCollections(params Collection[] collections);

        CommandStatus ProvisionCollections(ICollection<Collection> collections);
        #endregion Methods
    }
}
