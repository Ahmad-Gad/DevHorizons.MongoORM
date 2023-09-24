namespace DevHorizons.MongoORM.Context
{
    using Logging;
    public interface IMongoContext
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
        string Host { get; }

        Guid CorrelationId { get; }

        string DatabaseName { get; }

        bool Connected { get; }

        DateTime? ConnectionStartTime { get; }
        #endregion Properties
    }
}
