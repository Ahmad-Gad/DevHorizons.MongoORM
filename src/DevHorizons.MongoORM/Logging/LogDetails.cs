namespace DevHorizons.MongoORM.Logging
{
    using Microsoft.Extensions.Logging;

    public class LogDetails : ILogDetails
    {
        #region Constructors
        public LogDetails()
        {
        }

        public LogDetails(string host,
            string database,
            string collection,
            Guid? connectionCorrelationId,
            DateTime? connectionStartedTime,
            bool connected,
            Guid? collectionCorrelationId)
        {
            this.Host = host;
            this.Database = database;
            this.Collection = collection;
            this.ConnectionStartedTime = connectionStartedTime;
            this.Connected = connected;
            this.ConnectionCorrelationId = connectionCorrelationId;
            this.CollectionCorrelationId = collectionCorrelationId;
        }

        public LogDetails(string host,
            string database,
            string collection,
            Guid? connectionCorrelationId,
            DateTime? connectionStartedTime,
            bool connected,
            Guid? collectionCorrelationId,
            Exception ex)
            : this(host, database, collection, connectionCorrelationId, connectionStartedTime, connected, collectionCorrelationId)
        {
            if (ex is not null)
            {
                this.Exception = ex;
                this.StackTrace = ex.StackTrace;
                this.Message = ex.Message;
                this.LogLevel = LogLevel.Critical;
            }
        }
        #endregion Constructors

        #region Properties
        public string Host { get; set; }

        public string Database { get; set; }

        public string Collection { get; set; }

        public bool Connected { get; set; }

        public LogSource LogSource { get; set; }

        public int Code { get; set; }

        public string Message { get; set; }

        public string Description { get; set; }

        public string SourceMethod { get; set; }

        public string StackTrace { get; set; } = Environment.StackTrace;

        public Exception Exception { get; set; }

        public Guid? ConnectionCorrelationId { get; set; }

        public Guid? CollectionCorrelationId { get; set; }

        public DateTime? ConnectionStartedTime { get; set; }

        public DateTime Created { get; set; } = DateTime.UtcNow;

        public LogLevel LogLevel { get; set; }
        #endregion Properties
    }
}
