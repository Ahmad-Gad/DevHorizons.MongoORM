namespace DevHorizons.Mongo.Command
{
    using System.ComponentModel;
    using System.Linq.Expressions;

    using Attributes;

    using Connection;

    using Internal;

    using Logging;

    using Microsoft.Extensions.Logging;

    using MongoDB.Bson;
    using MongoDB.Bson.Serialization;
    using MongoDB.Driver;

    using Result;

    using Settings;

    /// <summary>
    ///    The mongo DB engine class to execute all the possible commands over the specified/mapped MongoDB collection.
    /// </summary>
    /// <typeparam name="T">The strongly typed class of the mapped collection.</typeparam>
    public class MongoCommandDynamic<T> : IMongoCommand<T>
    {
        #region Private Fields
        private readonly IMongoConnection mongoConnection;

        private readonly ILogger<IMongoCommand<T>> logger;

        private readonly string fullTypeName;

        private bool connected;

        private IMongoDatabase database;

        private IMongoCollection<BsonDocument> collection;

        private AggregateOptions aggregateOptions;
        #endregion Private Fields

        #region Constructors
        public MongoCommandDynamic(IMongoConnection mongoConnection)
        {
            this.mongoConnection = mongoConnection;
            this.fullTypeName = this.GetType().FullName;
            this.InitiateContext();
        }

        public MongoCommandDynamic(IMongoConnection mongoConnection, ILogger<IMongoCommand<T>> logger)
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
        public ICollection<ILogDetails> Errors { get; private set; } = new List<ILogDetails>();

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

        public void ClearErrors()
        {
            this.Errors.Clear();
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
                var result = this.database.RunCommand<T>(cmdJson, default, cancellationToken);
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

        public IResult<long> GetCount(Expression<Func<T, bool>> filter)
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

        public async Task<IResult<long>> GetCountAsync(Expression<Func<T, bool>> filter)
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

        public IResult<long> GetCount(FilterDefinition<T> filter)
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
                var serializerRegistry = BsonSerializer.SerializerRegistry;
                var documentSerializer = serializerRegistry.GetSerializer<T>();

                var bsonFilter = filter.Render(documentSerializer, serializerRegistry);
                var value = this.collection.CountDocuments(bsonFilter);
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

        public async Task<IResult<long>> GetCountAsync(FilterDefinition<T> filter)
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
        public IResult<ICollection<T>> GetAll(int pageSize = 0, int page = 0)
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
                var query = this.collection.Find(Builders<T>.Filter.Empty);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
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

        public async Task<IResult<ICollection<T>>> GetAllAsync(int pageSize = 0, int page = 0)
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
                var query = this.collection.Find(Builders<T>.Filter.Empty);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = await query.ToListAsync();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
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

        #region Get Filter
        public IResult<ICollection<T>> Get(Expression<Func<T, bool>> filter, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var query = this.collection.Find(filter);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public async Task<IResult<ICollection<T>>> GetAsync(Expression<Func<T, bool>> filter, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 1000;
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
                var query = this.collection.Find(filter);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = await query.ToListAsync();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetAsync));
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
                var sourceMethod = this.GetSourceName(nameof(this.GetAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 1001;

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

        public IResult<ICollection<T>> Get(FilterDefinition<T> filter, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var query = this.collection.Find(filter);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public async Task<IResult<ICollection<T>>> GetAsync(FilterDefinition<T> filter, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 1000;
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
                var query = this.collection.Find(filter);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = await query.ToListAsync();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.GetAsync));
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
                var sourceMethod = this.GetSourceName(nameof(this.GetAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 1001;

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
        #endregion Get Filter

        #region Get By Unique ID
        public IResult<T> Get(Guid id)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
                this.HandleError(error);
                return new Result<T>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var query = this.collection.Find(filter);

                var document = query.FirstOrDefault();
                return new Result<T>
                {
                    Value = document,
                    MatchedCount = document is null ? 0 : 1,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = ex.Code;

                return new Result<T>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

                return new Result<T>
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

        public async Task<IResult<T>> GetAsync(Guid id)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
                this.HandleError(error);
                return new Result<T>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = new BsonDocument(Constants.ID, id);
                //var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var query = this.collection.Find(filter);

                var document = await query.FirstOrDefaultAsync();
                return new Result<T>
                {
                    Value = default,
                    MatchedCount = document is null ? 0 : 1,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = ex.Code;

                return new Result<T>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

                return new Result<T>
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
        #endregion Get By Unique ID

        #region Get From Range
        public IResult<ICollection<T>> Get<TField>(Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var filter = Builders<T>.Filter.In(field, values);
                var query = this.collection.Find(filter);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public async Task<IResult<ICollection<T>>> GetAsync<TField>(Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var filter = Builders<T>.Filter.In(field, values);
                var query = this.collection.Find(filter);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = await query.ToListAsync();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public IResult<ICollection<T>> Get<TField>(FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var filter = Builders<T>.Filter.In(field, values);
                var query = this.collection.Find(filter);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public async Task<IResult<ICollection<T>>> GetAsync<TField>(FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var filter = Builders<T>.Filter.In(field, values);
                var query = this.collection.Find(filter);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = await query.ToListAsync();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public IResult<ICollection<T>> Get<TField>(Expression<Func<T, bool>> filter, Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var rangeFilter = Builders<T>.Filter.In(field, values);

                var filterGroup = Builders<T>.Filter.And(filter, rangeFilter);
                var query = this.collection.Find(filterGroup);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public async Task<IResult<ICollection<T>>> GetAsync<TField>(Expression<Func<T, bool>> filter, Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var rangeFilter = Builders<T>.Filter.In(field, values);

                var filterGroup = Builders<T>.Filter.And(filter, rangeFilter);
                var query = this.collection.Find(filterGroup);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = await query.ToListAsync();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public IResult<ICollection<T>> Get<TField>(Expression<Func<T, bool>> filter, FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var rangeFilter = Builders<T>.Filter.In(field, values);

                var filterGroup = Builders<T>.Filter.And(filter, rangeFilter);
                var query = this.collection.Find(filterGroup);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public async Task<IResult<ICollection<T>>> GetAsync<TField>(Expression<Func<T, bool>> filter, FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var rangeFilter = Builders<T>.Filter.In(field, values);

                var filterGroup = Builders<T>.Filter.And(filter, rangeFilter);
                var query = this.collection.Find(filterGroup);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = await query.ToListAsync();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public IResult<ICollection<T>> Get<TField>(FilterDefinition<T> filter, Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var rangeFilter = Builders<T>.Filter.In(field, values);

                var filterGroup = Builders<T>.Filter.And(filter, rangeFilter);
                var query = this.collection.Find(filterGroup);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public async Task<IResult<ICollection<T>>> GetAsync<TField>(FilterDefinition<T> filter, Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var rangeFilter = Builders<T>.Filter.In(field, values);

                var filterGroup = Builders<T>.Filter.And(filter, rangeFilter);
                var query = this.collection.Find(filterGroup);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = await query.ToListAsync();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public IResult<ICollection<T>> Get<TField>(FilterDefinition<T> filter, FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var rangeFilter = Builders<T>.Filter.In(field, values);

                var filterGroup = Builders<T>.Filter.And(filter, rangeFilter);
                var query = this.collection.Find(filterGroup);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = query.ToList();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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

        public async Task<IResult<ICollection<T>>> GetAsync<TField>(FilterDefinition<T> filter, FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to send the get/read query to the data source!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 100;
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
                var rangeFilter = Builders<T>.Filter.In(field, values);

                var filterGroup = Builders<T>.Filter.And(filter, rangeFilter);
                var query = this.collection.Find(filterGroup);
                if (pageSize != 0 && page != 0)
                {
                    query = query.Skip((page - 1) * pageSize).Limit(pageSize);
                }

                var list = await query.ToListAsync();
                return new Result<ICollection<T>>
                {
                    Value = list,
                    MatchedCount = list.Count,
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Get));
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
                var sourceMethod = this.GetSourceName(nameof(this.Get));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to get the documents based on the specified filter!";
                error.Code = 101;

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
        #endregion Get From Range
        #endregion Get

        #region Add
        public IResult<bool> Add(T document, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;

            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Add));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to insert the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 200;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                this.collection.InsertOne(document, default, cancellationToken);
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed,
                    AffectedCount = 1,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Add));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to add the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Add));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to add the specified document!";
                error.Code = 201;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }

        }

        public async Task<IResult<bool>> AddAsync(T document, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.AddAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to insert the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 2000;
                error.LogLevel = LogLevel.Error;

                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                await this.collection.InsertOneAsync(document, default, cancellationToken);
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed,
                    AffectedCount = 1,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.AddAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to add the specified document asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.AddAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to add the specified document asynchronously!";
                error.Code = 2001;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> AddMany(ICollection<T> documents, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.AddMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to insert the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 20000;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                this.collection.InsertMany(documents, default, cancellationToken);
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed,
                    AffectedCount = documents.Count,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.AddMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to add the specified documents!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.AddMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to add the specified documents!";
                error.Code = 20003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> AddManyAsync(ICollection<T> documents, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.AddMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to insert the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 20000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                await this.collection.InsertManyAsync(documents, default, cancellationToken);
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = CommandStatus.Executed | CommandStatus.Completed,
                    AffectedCount = documents.Count,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.AddMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to add the specified documents!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.AddMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to add the specified documents!";
                error.Code = 20003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion Add

        #region Update
        #region Update Document
        public IResult<bool> Update(Guid id, T document, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                ReplaceOptions replaceOptions = null;

                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var updateResult = this.collection.ReplaceOne(filter, document, replaceOptions, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync(Guid id, T document, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.UpdateAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 3000;
                error.LogLevel = LogLevel.Error;

                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                ReplaceOptions replaceOptions = null;
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var updateResult = await this.collection.ReplaceOneAsync(filter, document, replaceOptions, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                 CommandStatus.Executed | CommandStatus.Completed :
                 CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.UpdateAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.UpdateAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document asynchronously!";
                error.Code = 3001;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(Expression<Func<T, TField>> field, TField value, T document, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                ReplaceOptions replaceOptions = null;

                var filter = Builders<T>.Filter.Eq(field, value);
                var updateResult = this.collection.ReplaceOne(filter, document, replaceOptions, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, TField>> field, TField value, T document, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.UpdateAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 3000;
                error.LogLevel = LogLevel.Error;

                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                ReplaceOptions replaceOptions = null;
                var filter = Builders<T>.Filter.Eq(field, value);
                var updateResult = await this.collection.ReplaceOneAsync(filter, document, replaceOptions, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                 CommandStatus.Executed | CommandStatus.Completed :
                 CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.UpdateAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.UpdateAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document asynchronously!";
                error.Code = 3001;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(FieldDefinition<T, TField> field, TField value, T document, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                ReplaceOptions replaceOptions = null;

                var filter = Builders<T>.Filter.Eq(field, value);
                var updateResult = this.collection.ReplaceOne(filter, document, replaceOptions, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(FieldDefinition<T, TField> field, TField value, T document, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.UpdateAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 3000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                ReplaceOptions replaceOptions = null;
                var filter = Builders<T>.Filter.Eq(field, value);
                var updateResult = await this.collection.ReplaceOneAsync(filter, document, replaceOptions, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                 CommandStatus.Executed | CommandStatus.Completed :
                 CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.UpdateAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.UpdateAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document asynchronously!";
                error.Code = 3001;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion Update Document

        #region Update One Field
        #region By ID
        public IResult<bool> Update<TField>(Guid id, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = this.collection.UpdateOne(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(Guid id, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = await this.collection.UpdateOneAsync(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(Guid id, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = this.collection.UpdateOne(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(Guid id, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = await this.collection.UpdateOneAsync(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion By ID

        #region By Filter
        public IResult<bool> Update<TField>(Expression<Func<T, bool>> filter, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = this.collection.UpdateOne(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, bool>> filter, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = await this.collection.UpdateOneAsync(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(Expression<Func<T, bool>> filter, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = this.collection.UpdateOne(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, bool>> filter, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = await this.collection.UpdateOneAsync(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(FilterDefinition<T> filter, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = this.collection.UpdateOne(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(FilterDefinition<T> filter, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = await this.collection.UpdateOneAsync(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(FilterDefinition<T> filter, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = this.collection.UpdateOne(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(FilterDefinition<T> filter, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var update = Builders<T>.Update.Set(field, value);
                var updateResult = await this.collection.UpdateOneAsync(filter, update, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        #endregion By Filter
        #endregion Update One Field

        #region Update Multiple Fields
        #region By ID
        public IResult<bool> Update<TField>(Guid id, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = this.collection.UpdateMany(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(Guid id, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = await this.collection.UpdateManyAsync(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(Guid id, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = this.collection.UpdateMany(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(Guid id, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = await this.collection.UpdateManyAsync(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update(Guid id, IDictionary<string, object> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = this.collection.UpdateMany(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync(Guid id, IDictionary<string, object> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = await this.collection.UpdateManyAsync(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion By ID

        #region By Filter
        public IResult<bool> Update<TField>(Expression<Func<T, bool>> filter, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = this.collection.UpdateMany(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, bool>> filter, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = await this.collection.UpdateManyAsync(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(Expression<Func<T, bool>> filter, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = this.collection.UpdateMany(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, bool>> filter, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = await this.collection.UpdateManyAsync(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update(Expression<Func<T, bool>> filter, IDictionary<string, object> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = this.collection.UpdateMany(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync(Expression<Func<T, bool>> filter, IDictionary<string, object> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = await this.collection.UpdateManyAsync(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(FilterDefinition<T> filter, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = this.collection.UpdateMany(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(FilterDefinition<T> filter, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = await this.collection.UpdateManyAsync(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update<TField>(FilterDefinition<T> filter, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = this.collection.UpdateMany(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync<TField>(FilterDefinition<T> filter, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = await this.collection.UpdateManyAsync(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Update(FilterDefinition<T> filter, IDictionary<string, object> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = this.collection.UpdateMany(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> UpdateAsync(FilterDefinition<T> filter, IDictionary<string, object> pairs, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to update the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 300;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var updateDefinitionList = new List<UpdateDefinition<T>>();
                foreach (var pair in pairs)
                {
                    updateDefinitionList.Add(Builders<T>.Update.Set(pair.Key, pair.Value));
                }

                var updateGroup = Builders<T>.Update.Combine(updateDefinitionList);
                var updateResult = await this.collection.UpdateManyAsync(filter, updateGroup, default, cancellationToken);
                var status = updateResult.MatchedCount > 0 && updateResult.ModifiedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    CommandStatus = status,
                    AffectedCount = updateResult.ModifiedCount,
                    MatchedCount = updateResult.MatchedCount,
                    Value = true
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Update));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to update the specified document!";
                error.Code = 301;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion By Filter
        #endregion Update Multiple Fields
        #endregion Update

        #region Delete
        #region Delete Single
        public IResult<bool> Delete(Guid id, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Delete));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 400;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var deleteResult = collection.DeleteOne(filter, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Delete));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Delete));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter!";
                error.Code = 401;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 4000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(Constants.ID, id);
                var deleteResult = await this.collection.DeleteOneAsync(filter, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter asynchronously!";
                error.Code = 4002;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }

        }

        public IResult<bool> Delete<TField>(Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Delete));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 400;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(field, value);
                var deleteResult = collection.DeleteOne(filter, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Delete));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Delete));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter!";
                error.Code = 401;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteAsync<TField>(Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 4000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(field, value);
                var deleteResult = await this.collection.DeleteOneAsync(filter, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter asynchronously!";
                error.Code = 4002;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> Delete<TField>(FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.Delete));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 400;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(field, value);
                var deleteResult = collection.DeleteOne(filter, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Delete));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.Delete));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter!";
                error.Code = 401;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteAsync<TField>(FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 4000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);
                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var filter = Builders<T>.Filter.Eq(field, value);
                var deleteResult = await this.collection.DeleteOneAsync(filter, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target document based on the specified filter asynchronously!";
                error.Code = 4002;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion Delete Single

        #region Delete Many
        #region Delete Many By Filter
        public IResult<bool> DeleteMany(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var deleteResult = this.collection.DeleteMany(filter, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified filter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified filter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteManyAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteManyAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40001;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var deleteResult = await this.collection.DeleteManyAsync(filter, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteManyAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified filter asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteManyAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified filter asynchronously!";
                error.Code = 40004;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> DeleteMany(FilterDefinition<T> filter, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var deleteResult = this.collection.DeleteMany(filter, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified filter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteMany));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified filter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteManyAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40001;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var deleteResult = await this.collection.DeleteManyAsync(filter, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteManyAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified filter asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteManyAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified filter asynchronously!";
                error.Code = 40004;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion Delete Many By Filter

        #region Delete Many By Filter & Range Field/Value
        public IResult<bool> DeleteByRange<TField>(Expression<Func<T, bool>> primaryFilter, Expression<Func<T, TField>> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var rangeFilter = Builders<T>.Filter.In(field, rangeValues);
                var filterGroup = Builders<T>.Filter.And(primaryFilter, rangeFilter);

                var deleteResult = this.collection.DeleteMany(filterGroup, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteByRangeAsync<TField>(Expression<Func<T, bool>> primaryFilter, Expression<Func<T, TField>> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var rangeFilter = Builders<T>.Filter.In(field, rangeValues);
                var filterGroup = Builders<T>.Filter.And(primaryFilter, rangeFilter);

                var deleteResult = await this.collection.DeleteManyAsync(filterGroup, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> DeleteByRange<TField>(Expression<Func<T, bool>> primaryFilter, FieldDefinition<T, TField> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var rangeFilter = Builders<T>.Filter.In(field, rangeValues);
                var filterGroup = Builders<T>.Filter.And(primaryFilter, rangeFilter);

                var deleteResult = this.collection.DeleteMany(filterGroup, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteByRangeAsync<TField>(Expression<Func<T, bool>> primaryFilter, FieldDefinition<T, TField> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var rangeFilter = Builders<T>.Filter.In(field, rangeValues);
                var filterGroup = Builders<T>.Filter.And(primaryFilter, rangeFilter);

                var deleteResult = await this.collection.DeleteManyAsync(filterGroup, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> DeleteByRange<TField>(FilterDefinition<T> primaryFilter, Expression<Func<T, TField>> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var rangeFilter = Builders<T>.Filter.In(field, rangeValues);
                var filterGroup = Builders<T>.Filter.And(primaryFilter, rangeFilter);

                var deleteResult = this.collection.DeleteMany(filterGroup, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteByRangeAsync<TField>(FilterDefinition<T> primaryFilter, Expression<Func<T, TField>> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var rangeFilter = Builders<T>.Filter.In(field, rangeValues);
                var filterGroup = Builders<T>.Filter.And(primaryFilter, rangeFilter);

                var deleteResult = await this.collection.DeleteManyAsync(filterGroup, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IResult<bool> DeleteByRange<TField>(FilterDefinition<T> primaryFilter, FieldDefinition<T, TField> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var rangeFilter = Builders<T>.Filter.In(field, rangeValues);
                var filterGroup = Builders<T>.Filter.And(primaryFilter, rangeFilter);

                var deleteResult = this.collection.DeleteMany(filterGroup, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRange));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteByRangeAsync<TField>(FilterDefinition<T> primaryFilter, FieldDefinition<T, TField> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete the specified document(s)!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40000;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Value = false,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var rangeFilter = Builders<T>.Filter.In(field, rangeValues);
                var filterGroup = Builders<T>.Filter.And(primaryFilter, rangeFilter);

                var deleteResult = await this.collection.DeleteManyAsync(filterGroup, default, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;

                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteByRangeAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete the target documents based on the specified primaryFilter!";
                error.Code = 40003;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion Delete Many By Filter & Range Field/Value

        #region Delete All
        public IResult<bool> DeleteAll(CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAll));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete all the documents!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40001;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var deleteResult = this.collection.DeleteMany(Builders<T>.Filter.Empty, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAll));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete all the documents!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAll));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete all the documents!";
                error.Code = 40004;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public async Task<IResult<bool>> DeleteAllAsync(CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;
            if (!this.connected || this.collection is null)
            {
                error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAllAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to delete all the documents asynchronously!";
                error.Description = $"Failed to connect with the specified collection ({this.CollectionName}) in the database ({this.database?.DatabaseNamespace?.DatabaseName}).";
                error.Code = 40001;
                error.LogLevel = LogLevel.Error;
                this.HandleError(error);

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var deleteResult = this.collection.DeleteMany(Builders<T>.Filter.Empty, cancellationToken);
                var status = deleteResult.DeletedCount > 0 ?
                    CommandStatus.Executed | CommandStatus.Completed :
                    CommandStatus.Executed;
                return new Result<bool>
                {
                    Success = true,
                    Value = true,
                    CommandStatus = status,
                    AffectedCount = deleteResult.DeletedCount,
                    MatchedCount = deleteResult.DeletedCount
                };
            }
            catch (MongoCommandException ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAllAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete all the documents asynchronously!";
                error.Code = ex.Code;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                var sourceMethod = this.GetSourceName(nameof(this.DeleteAllAsync));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "Failed to delete all the documents asynchronously!";
                error.Code = 40004;
                error.LogLevel = LogLevel.Error;

                return new Result<bool>
                {
                    Success = false,
                    Value = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
            finally
            {
                this.HandleError(error);
            }
        }
        #endregion Delete All
        #endregion Delete Many
        #endregion Delete
        #endregion DAO Methods
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

            this.Errors.Add(error);
            this.ErrorRaised?.Invoke(error);
            this.logger?.Log(error.LogLevel, error.Message, error);
        }

        private ILogDetails InitializeError(Exception ex = null)
        {
            var error = new LogDetails(this.mongoConnection.Host,
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

                if (string.IsNullOrWhiteSpace(cm.DatabaseName) || string.IsNullOrWhiteSpace(cm.CollectionName))
                {
                    mca = ExtensionMethods.GetCustomAttribute<T, MongoCollectionAttribute>();
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