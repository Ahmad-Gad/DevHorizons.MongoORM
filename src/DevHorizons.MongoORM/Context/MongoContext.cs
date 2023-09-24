namespace DevHorizons.MongoORM.Context
{
    using System.Reflection;

    using Attributes;

    using Command;

    using Connection;

    using Logging;

    using Microsoft.Extensions.Logging;

    public class MongoContext : IMongoContext
    {
        #region Private Fields
        private readonly IMongoConnection mongoConnection;

        private readonly ILogger<MongoContext> logger;
        #endregion Private Fields

        #region Constructors
        public MongoContext(IMongoConnection mongoConnection)
        {
            this.mongoConnection = mongoConnection;
            this.InitializeContexts();
        }

        public MongoContext(IMongoConnection mongoConnection, ILogger<MongoContext> logger)
             : this(mongoConnection)
        {
            this.logger = logger;
        }
        #endregion Constructors

        #region Delegates
        /// <summary>
        ///    A delegate to handel the error raised by the class.
        /// </summary>
        /// <param name="error">The raised error.</param>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>08/11/2018 11:13 AM</DateTime>
        /// </Created>
        public delegate void RaiseError(ILogDetails error);
        #endregion Delegates

        #region Properties
        public Guid CorrelationId { get; private set; }

        public string DatabaseName { get; private set; }

        public bool Connected { get; private set; }

        public DateTime? ConnectionStartTime { get; private set; }

        public string Host { get; private set; }

        #endregion Properties

        #region Event Handlers
        public event IMongoContext.RaiseError ErrorRaised;
        #endregion Event Handlers

        #region Private Methods
        private void InitializeContexts()
        {
            if (this.mongoConnection is null || !this.mongoConnection.Connected)
            {
                return;
            }

            this.Connected = this.mongoConnection.Connected;
            this.DatabaseName = this.mongoConnection?.Database?.DatabaseNamespace?.DatabaseName;
            this.CorrelationId = this.mongoConnection.CorrelationId;
            this.ConnectionStartTime = this.mongoConnection.StartTime;
            this.Host = this.mongoConnection.Host;

            try
            {
                var type = this.GetType();
                var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                foreach (var prop in props)
                {
                    if (prop.PropertyType.GetInterfaces().Any(i => i.UnderlyingSystemType == typeof(IMongoCommandBase)))
                    {
                        var value = Activator.CreateInstance(prop.PropertyType, this.mongoConnection, this.logger);
                        var collectionModel = (MongoCollectionAttribute)prop.GetCustomAttributes(typeof(MongoCollectionAttribute), true).FirstOrDefault();
                        if (collectionModel is not null && !string.IsNullOrWhiteSpace(collectionModel.Collection))
                        {
                            var method = value.GetType().GetMethod(nameof(IMongoCommandBase.SetCollection), BindingFlags.Public | BindingFlags.Instance);
                            if (method is not null)
                            {
                                method.Invoke(value, new object[] { collectionModel.Collection });
                            }
                        }

                        prop.SetValue(this, value);
                    }
                }
            }
            catch (Exception ex)
            {
                var error = new LogDetails
                {
                    Exception = ex,
                    LogLevel = LogLevel.Critical,
                    Code = -5000,
                    Message = ex.Message,
                    Description = "Failed to initialize the mongo Contexts!",
                    SourceMethod = $"{this.GetType().FullName}.{nameof(this.InitializeContexts)}",
                    Connected = this.mongoConnection.Connected,
                    Database = this.mongoConnection?.Database?.DatabaseNamespace?.DatabaseName,
                    Host = this.mongoConnection?.Host,
                    StackTrace = ex.StackTrace,
                    ConnectionCorrelationId = this.mongoConnection?.CorrelationId,
                    ConnectionStartedTime = this.mongoConnection?.StartTime,
                    LogSource = LogSource.OtherException
                };

                this.HandleError(error);
            }

        }

        private void HandleError(ILogDetails error)
        {
            if (error == null)
            {
                return;
            }

            this.ErrorRaised?.Invoke(error);
            this.logger?.Log(error.LogLevel, error.Message, error);
        }
        #endregion Private Methods
    }
}
