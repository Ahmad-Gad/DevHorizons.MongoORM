namespace DevHorizons.MongoORM.Command
{
    using Logging;

    using Settings;

    public interface IMongoCommandBase
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

        string DatabaseName { get; }

        string CollectionName { get; }

        Guid CorrelationId { get; }

        string Host { get; }

        Guid ConnectionCorrelationId { get; }

        bool Connected { get; }

        DateTime? ConnectionStartTime { get; }
        #endregion Properties

        #region Public Methods
        #region Operation Methods
        bool IsConnected();

        bool SetDatabase(string databaseName);

        bool SetCollection(string collectionName);

        void SetCollation(MongoCollationSettings collationSettings);

        bool SetJsonSchemaValidation(string jsonSchema, CancellationToken cancellationToken = default);
        #endregion Operation Methods
        #endregion Public Methods
    }
}
