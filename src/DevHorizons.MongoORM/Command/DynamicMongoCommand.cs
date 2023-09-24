namespace DevHorizons.MongoORM.Command
{
    using Connection;

    using DevHorizons.MongoORM.Attributes;
    using DevHorizons.MongoORM.Internal;
    using DevHorizons.MongoORM.Result;
    using DevHorizons.MongoORM.Settings;

    using Logging;

    using Microsoft.Extensions.Logging;

    using MongoDB.Bson;
    using MongoDB.Driver;

    using System.ComponentModel;
    using System.Linq.Expressions;
    using System.Text.Json;

    public class DynamicMongoCommand
    {
        #region Private Fields
        private readonly IMongoConnection mongoConnection;

        private readonly ILogger<DynamicMongoCommand> logger;

        private readonly string fullTypeName;

        private bool connected;

        private IMongoDatabase database;

        private IMongoCollection<BsonDocument> collection;

        private AggregateOptions aggregateOptions;
        #endregion Private Fields

        #region Constructors
        public DynamicMongoCommand(IMongoConnection mongoConnection)
        {
            this.mongoConnection = mongoConnection;
            this.fullTypeName = this.GetType().FullName;
            this.InitiateContext();
        }

        public DynamicMongoCommand(IMongoConnection mongoConnection, ILogger<DynamicMongoCommand> logger)
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

        #region Event Handlers
        public event IMongoCommandBase.RaiseError ErrorRaised;
        #endregion Event Handlers

        #region Properties

        public string DatabaseName { get; private set; }

        public string CollectionName { get; private set; }

        public Guid CorrelationId { get; private set; }

        public IMongoCollection<BsonDocument> Collection
        {
            get
            {
                return this.collection;
            }
        }

        public string Host { get; private set; }

        public Guid ConnectionCorrelationId { get; private set; }

        public bool Connected { get; private set; }

        public DateTime? ConnectionStartTime { get; private set; }
        #endregion Properties

        #region Public Methods
        #region Operation Methods
        public bool IsConnected()
        {
            return this.IsConnected(false);
        }

        public bool SetDatabase(string databaseName)
        {
            return this.SetDatabase(databaseName, false);
        }

        public bool SetCollection(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                var error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.SetCollection));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "The collection name cannot be null or empty string!";
                error.Message = $"Failed to connect with the specified collection ({collectionName})!";
                error.Code = -311;
                error.LogLevel = LogLevel.Critical;
                this.HandleError(error);
                return false;
            }

            if (this.database is null)
            {
                var error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.SetCollection));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to connect with the specified collection ({collectionName})";
                error.Description = $"The mongo database is not specified/initialized in the host {this.mongoConnection.Host}!";
                error.Code = -312;
                this.HandleError(error);
                return false;
            }

            var matchFound = this.database.ListCollectionNames().ToList().Any(c => c == collectionName);

            if (!matchFound)
            {
                var error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.SetCollection));
                error.LogSource = LogSource.Connection;
                error.Message = $"Failed to connect with the collection ({collectionName})";
                error.Description = $"The specified collection ({collectionName}) is not found the specified database ({this.database.DatabaseNamespace.DatabaseName}) in the host {this.mongoConnection.Host}!";
                error.Code = -315;
                error.LogLevel = LogLevel.Critical;
                this.HandleError(error);
                return false;
            }

            this.CollectionName = collectionName;
            try
            {
                var coll = this.database.GetCollection<BsonDocument>(collectionName);
                this.collection = coll;
                this.connected = true;
            }
            catch (Exception ex)
            {
                var error = this.InitializeError(ex);
                error.SourceMethod = this.GetSourceName(nameof(this.SetCollection));
                error.LogSource = LogSource.Connection;
                error.Description = $"Failed to communicate with the collection ({collectionName}) is not found the specified database ({this.database.DatabaseNamespace.DatabaseName}) in the host {this.mongoConnection.Host}!";
                error.Code = -325;
                error.LogLevel = LogLevel.Critical;
                this.HandleError(error);
                return false;
            }

            return true;
        }

        public void SetCollation(MongoCollationSettings collationSettings)
        {
            var collation = new Collation(locale: GetLocaleValue(collationSettings.Locale),
               strength: (CollationStrength)collationSettings.CollationStrength,
               caseLevel: collationSettings.CaseLevel,
               caseFirst: (CollationCaseFirst)collationSettings.CollationCaseFirst);
            this.aggregateOptions = new AggregateOptions { Collation = collation };
        }

        public bool SetJsonSchemaValidation(string jsonSchema, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;

            if (!this.connected)
            {
                error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.SetJsonSchemaValidation));
                error.LogSource = LogSource.Connection;
                error.Message = "Failed to set the json schema validation!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.DatabaseName}) in the host ({this.mongoConnection.Host})!";
                error.Code = -350;
                this.HandleError(error);
                return false;
            }

            try
            {
                var cmdJson = $"{{collMod: '{this.CollectionName}', validator:}}";
                var result = this.database.RunCommand<BsonDocument>(cmdJson, default, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                error.Code = -3500;
                error.SourceMethod = this.GetSourceName(nameof(this.SetJsonSchemaValidation));
                error.LogSource = LogSource.Command;
                error.Description = $"Failed to set the json schema validation on the collection ({this.CollectionName}) on the database ({this.DatabaseName}) on the host ({this.mongoConnection.Host}). The specified json schema: {jsonSchema}!";
                this.HandleError(error);
                return false;
            }
        }
        #endregion Operation Methods

        #region DAO Methods

        #endregion DAO Methods
        #region Aggregation
        public IResult<long> GetTotalCount()
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetTotalCount));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = "Failed to get the total documents count!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 1110;
                this.HandleError(error);
                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var value = this.collection.CountDocuments(Builders<BsonDocument>.Filter.Empty);
                return new Result<long>
                {
                    Value = value,
                    MatchedCount = value,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetTotalCount));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the total documents count!";
                error.Code = ex.Code;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetTotalCount));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 1111;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<long>> GetTotalCountAsync()
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetTotalCountAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = "Failed to get the total documents count!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 1110;
                this.HandleError(error);
                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var value = await this.collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
                return new Result<long>
                {
                    Value = value,
                    MatchedCount = value,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetTotalCountAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the total documents count!";
                error.Code = ex.Code;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetTotalCountAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the total documents count!";
                error.Code = 1111;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<long> GetCount(Expression<Func<BsonDocument, bool>> filter)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetCount));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = "Failed to get the documents count based on the specified filter!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 1110;
                this.HandleError(error);
                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var value = this.collection.CountDocuments(filter);
                return new Result<long>
                {
                    Value = value,
                    MatchedCount = value,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetCount));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents count based on the specified filter!";
                error.Code = ex.Code;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetCount));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents count based on the specified filter!";
                error.Code = 1111;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<long>> GetCountAsync(Expression<Func<BsonDocument, bool>> filter)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetCountAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = "Failed to get the documents count based on the specified filter!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 1110;
                this.HandleError(error);
                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var value = await this.collection.CountDocumentsAsync(filter);
                return new Result<long>
                {
                    Value = value,
                    MatchedCount = value,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetCountAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents count based on the specified filter!";
                error.Code = ex.Code;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetCountAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents count based on the specified filter!";
                error.Code = 1111;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<long> GetCount(FilterDefinition<BsonDocument> filter)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetCount));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = "Failed to get the documents count based on the specified filter!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 1110;
                this.HandleError(error);
                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var value = this.collection.CountDocuments(filter);
                return new Result<long>
                {
                    Value = value,
                    MatchedCount = value,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetCount));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents count based on the specified filter!";
                error.Code = ex.Code;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetCount));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents count based on the specified filter!";
                error.Code = 1111;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<long>> GetCountAsync(FilterDefinition<BsonDocument> filter)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetCountAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = "Failed to get the documents count based on the specified filter!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 1110;
                this.HandleError(error);
                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var value = await this.collection.CountDocumentsAsync(filter);
                return new Result<long>
                {
                    Value = value,
                    MatchedCount = value,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetCountAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents count based on the specified filter!";
                error.Code = ex.Code;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetCountAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents count based on the specified filter!";
                error.Code = 1111;

                return new Result<long>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion Aggregation

        #region Get
        #region Get All
        public IResult<ICollection<T>> GetAll<T>(int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetAll));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 1100;
                this.HandleError(error);
                return new Result<ICollection<T>>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var query = this.collection.Find(Builders<BsonDocument>.Filter.Empty);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                //return new Result<ICollection<T>>
                //{
                //    Value = list,
                //    MatchedCount = list.Count,
                //    Success = true,
                //    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                //};

                return null;
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetAll));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = ex.Code;

                return new Result<ICollection<T>>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetAll));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 1101;

                return new Result<ICollection<T>>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion Get All
        #endregion Get
        #endregion Public Methods

        #region Private Methods
        private void InitiateContext()
        {
            this.CorrelationId = Guid.NewGuid();

            if (this.mongoConnection is null)
            {
                var error = this.InitializeError();
                error.Code = -2001;
                error.SourceMethod = this.GetSourceName(nameof(this.InitiateContext));
                error.LogSource = LogSource.Connection;
                error.Description = $"Failed to connect with the host because the '{nameof(MongoConnection)}' is not specified!";
                error.LogLevel = LogLevel.Critical;
                this.HandleError(error);
                return;
            }

            this.Host = this.mongoConnection.Host;
            this.Connected = this.mongoConnection.Connected;
            this.ConnectionStartTime = this.mongoConnection.StartTime;

            if (!this.mongoConnection.Connected)
            {
                var error = this.InitializeError();
                error.Code = -2002;
                error.SourceMethod = this.GetSourceName(nameof(this.InitiateContext));
                error.LogSource = LogSource.Connection;
                error.Description = "The connection with the host is not initialized!";
                error.LogLevel = LogLevel.Critical;
                this.HandleError(error);
                return;
            }

            this.SetCollation(this.mongoConnection.CollationSettings);
            var cm = this.GetCollectionModel();
            if (string.IsNullOrWhiteSpace(cm.DatabaseName))
            {
                return;
            }

            if (this.database is null)
            {
                var setDatabase = this.SetDatabase(cm.DatabaseName, true);
                if (!setDatabase)
                {
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(cm.CollectionName))
            {
                return;
            }

            this.CollectionName = cm.CollectionName;
            var setCollection = this.SetCollection(cm.CollectionName);

            if (!setCollection)
            {
                return;
            }

            if (this.database is not null && this.collection is not null)
            {
                this.connected = this.IsConnected(true);
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

        private ILogDetails InitializeError(Exception ex = null)
        {
            var error = new LogDetails(
                this.mongoConnection.Host,
                this.database?.DatabaseNamespace?.DatabaseName,
                this.CollectionName,
                this.mongoConnection?.CorrelationId,
                this.mongoConnection?.StartTime,
                this.mongoConnection is null ? false : this.mongoConnection.Connected,
                this.CorrelationId,
                ex);

            error.LogLevel = LogLevel.Critical;
            return error;
        }

        private string GetSourceName(string methodName)
        {
            return $"{this.fullTypeName}.{methodName}";
        }

        private ICollectionModel GetCollectionModel()
        {
            var cm = new CollectionModel
            {
                DatabaseName = this.DatabaseName,
                CollectionName = this.CollectionName
            };

            if (string.IsNullOrWhiteSpace(cm.DatabaseName) || string.IsNullOrWhiteSpace(cm.CollectionName))
            {
                var mca = this.GetCustomAttribute<MongoCollectionAttribute>();
                if (mca is not null)
                {
                    if (string.IsNullOrWhiteSpace(cm.DatabaseName))
                    {
                        cm.DatabaseName = mca.Database;
                    }

                    if (string.IsNullOrWhiteSpace(cm.CollectionName))
                    {
                        cm.CollectionName = mca.Collection;
                    }
                }

                if (string.IsNullOrWhiteSpace(cm.DatabaseName) && this.mongoConnection.Database is not null)
                {
                    cm.DatabaseName = this.mongoConnection.Database.DatabaseNamespace.DatabaseName;
                    this.database = this.mongoConnection.Database;
                }
            }

            return cm;
        }

        private bool SetDatabase(string databaseName, bool force)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                var error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.SetDatabase));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to connect with the specified database ({databaseName})!";
                error.Description = "The database name cannot be null or empty string!";
                error.Code = -211;
                error.LogLevel = LogLevel.Critical;
                this.HandleError(error);
                return false;
            }

            if (!force && !this.connected)
            {
                var error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.SetDatabase));
                error.LogSource = LogSource.Connection;
                error.Message = $"Failed to connect with the specified database ({databaseName})!";
                error.Description = $"Failed to connect with the specified database ({databaseName}) because the server is either not up and running or the host ({this.mongoConnection.Host}) is not reachable!";
                error.Code = -2002;
                error.LogLevel = LogLevel.Critical;
                this.HandleError(error);
                return false;
            }

            var db = this.mongoConnection.GetDatabase(databaseName);
            if (db is null)
            {
                return false;
            }

            this.DatabaseName = databaseName;
            this.database = db;
            return true;
        }

        private bool IsConnected(bool force)
        {
            if (this.mongoConnection is null || !this.mongoConnection.Connected || (!this.connected && !force) || this.database is null)
            {
                return false;
            }

            return this.mongoConnection.Ping(this.database);
        }
        #region Static Methods

        private static string GetLocaleValue(MongoLocale mongoLocale)
        {
            var memberInfo = mongoLocale.GetType().GetMember(mongoLocale.ToString()).FirstOrDefault();
            var descriptionAttribute = (DescriptionAttribute)memberInfo.GetCustomAttributes(typeof(DescriptionAttribute), false).FirstOrDefault();

            if (descriptionAttribute is not null)
            {
                var localeValue = descriptionAttribute.Description;
                return localeValue;
            }

            return "en";
        }
        #endregion Static Methods
        #endregion Private Methods
    }
}
