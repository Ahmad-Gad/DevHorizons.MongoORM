namespace DevHorizons.MongoORM.Connection
{
    using System;
    using System.Linq;

    using Internal;

    using Logging;

    using Microsoft.Extensions.Logging;

    using MongoDB.Bson;
    using MongoDB.Driver;

    using Provisioning;
    using Provisioning.Index;
    using Provisioning.Validation;

    using Result;

    using Result.CommandListCollectionInfo;

    using Settings;

    public class MongoConnection : IMongoConnection
    {
        #region Private Fields
        private readonly IMongoConnectionSettings connectionSettings;

        private readonly ILogger<MongoConnection> logger;

        private readonly IProvisioningSettings provisioningSettings;

        private readonly string fullTypeName;

        private IMongoClient client;
        #endregion Private Fields

        #region Constructors
        public MongoConnection(IMongoConnectionSettings connectionSettings)
        {
            this.connectionSettings = connectionSettings;
            this.fullTypeName = this.GetType().FullName;
            this.InitiateConnection();
        }

        public MongoConnection(IMongoConnectionSettings connectionSettings, ILogger<MongoConnection> logger)
            : this(connectionSettings)
        {
            this.logger = logger;
        }

        public MongoConnection(IMongoConnectionSettings connectionSettings, IProvisioningSettings provisioningSettings)
            : this(connectionSettings)
        {
            if (provisioningSettings is not null && provisioningSettings.Collections is not null && provisioningSettings.Collections.Count != 0)
            {
                this.provisioningSettings = provisioningSettings;
                this.InitiateProvisioning(provisioningSettings.Collections);
            }
        }

        public MongoConnection(IMongoConnectionSettings connectionSettings, IProvisioningSettings provisioningSettings, ILogger<MongoConnection> logger)
            : this(connectionSettings, provisioningSettings)
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
        public event IMongoConnection.RaiseError ErrorRaised;
        #endregion Event Handlers

        #region Properties
        public MongoCollationSettings CollationSettings { get; private set; } = new MongoCollationSettings();

        public Guid CorrelationId { get; private set; } = Guid.NewGuid();

        public DateTime StartTime { get; private set; } = DateTime.UtcNow;

        public bool Connected { get; private set; }

        public string Host { get; private set; }

        public ICollection<ILogDetails> Errors { get; private set; } = new List<ILogDetails>();

        public IMongoDatabase Database { get; private set; }
        #endregion Properties

        #region Public Methods
        public bool Ping(string databaseName)
        {
            if (!Connected)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return false;
            }

            try
            {
                var db = client.GetDatabase(databaseName);
                return this.Ping(db);
            }
            catch (Exception ex)
            {
                var error = this.InitializeError(ex);
                error.SourceMethod = this.GetSourceName(nameof(this.Ping));
                error.LogSource = LogSource.Connection;
                error.Description = $"Failed to communciate with the specified database {databaseName}!";
                error.Code = -206;
                HandleError(error);
                return false;
            }
        }

        public bool Ping(IMongoDatabase mongoDatabase)
        {
            if (mongoDatabase is null || mongoDatabase.DatabaseNamespace is null)
            {
                return false;
            }

            try
            {
                var isMongoLive = mongoDatabase.RunCommandAsync((Command<BsonDocument>)"{ping:1}").Wait(1000);
                return isMongoLive;
            }
            catch (Exception ex)
            {
                var error = this.InitializeError(ex);
                error.SourceMethod = this.GetSourceName(nameof(this.Ping));
                error.LogSource = LogSource.Connection;
                error.Description = error.Description = $"Failed to communciate with the specified database {mongoDatabase.DatabaseNamespace?.DatabaseName}!"; ;
                error.Code = -205;
                HandleError(error);
                return false;
            }
        }

        public void ClearErrors()
        {
            Errors.Clear();
        }

        public IMongoDatabase GetDatabase(string databaseName)
        {
            ILogDetails error = null;
            if (!this.Connected)
            {
                error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.GetDatabase));
                error.LogSource = LogSource.Connection;
                error.Description = $"Failed to connect with the specified database ({databaseName}) because the server is either not up and running or the host ({Host}) is not reachable!";
                error.Code = -200;
                this.HandleError(error);
                return null;
            }

            try
            {
                var matchFound = this.client.ListDatabaseNames().ToList().Any(d => d.Equals(databaseName, StringComparison.InvariantCultureIgnoreCase));

                if (!matchFound)
                {
                    error = this.InitializeError();
                    error.SourceMethod = this.GetSourceName(nameof(this.GetDatabase));
                    error.LogSource = LogSource.Connection;
                    error.Description = $"The specified database ({databaseName}) is not found in the host ({Host})!";
                    error.Code = -201;
                    return null;
                }

                var db = this.client.GetDatabase(databaseName);
                return db;
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                error.Code = -202;
                error.SourceMethod = this.GetSourceName(nameof(this.GetDatabase));
                error.LogSource = LogSource.Connection;
                error.Description = $"Failed to connect with the specified database ({databaseName})!";
                return null;
            }
            finally
            {
                this.HandleError(error);
            }
        }

        public IMongoCollection<T> GetCollection<T>(IMongoDatabase database, string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                var error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetCollection));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "The collection name cannot be null or empty string!";
                error.Message = $"Failed to connect with the specified collection ({collectionName}) in the host '{this.Host}'!";
                error.Code = -3011;
                this.HandleError(error);
                return null;
            }

            if (database is null)
            {
                var error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetCollection));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to connect with the specified collection ({collectionName})";
                error.Description = $"The mongo database is not specified/initialized in the host '{this.Host}'!";
                error.Code = -3012;
                this.HandleError(error);
                return null;
            }

            if (!this.Connected)
            {
                var error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.GetCollection));
                error.LogSource = LogSource.Connection;
                error.Message = $"Failed to connect with the specifiedcollection ({collectionName})";
                error.Description = $"Failed to connect with the specified database ({database.DatabaseNamespace.DatabaseName}) because the server is either not up and running or the host ({this.Host}) is not reachable!";
                error.Code = -3013;
                this.HandleError(error);
                return null;
            }


            var matchFound = database.ListCollectionNames().ToList().Any(c => c == collectionName);

            if (!matchFound)
            {
                var error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.GetCollection));
                error.LogSource = LogSource.Connection;
                error.Message = $"Failed to connect with the collection ({collectionName})";
                error.Description = $"The specified collection ({collectionName}) is not found the specified database ({database.DatabaseNamespace.DatabaseName}) in the host {this.Host}!";
                error.Code = -3015;
                return null;
            }

            var coll = database.GetCollection<T>(collectionName);
            return coll;
        }

        public IMongoCollection<T> GetCollection<T>(string databaseName, string collectionName)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                var error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetCollection));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "The database name cannot be null or empty string!";
                error.Message = $"Failed to connect with the specified collection ({collectionName}) in the host '{this.Host}'!";
                error.Code = -30011;
                this.HandleError(error);
                return null;
            }

            if (string.IsNullOrWhiteSpace(collectionName))
            {
                var error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetCollection));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Description = "The collection name cannot be null or empty string!";
                error.Message = $"Failed to connect with the specified collection ({collectionName}) in the host '{this.Host}'!";
                error.Code = -30011;
                this.HandleError(error);
                return null;
            }

            var database = this.GetDatabase(databaseName);
            if (database is null)
            {
                var error = this.InitializeError();
                var sourceMethod = this.GetSourceName(nameof(this.GetCollection));
                error.SourceMethod = sourceMethod;
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to connect with the specified collection ({collectionName})";
                error.Description = $"Cannot access the database in the host '{this.Host}'!";
                error.Code = -30012;
                this.HandleError(error);
                return null;
            }

            if (!this.Connected)
            {
                var error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.GetCollection));
                error.LogSource = LogSource.Connection;
                error.Message = $"Failed to connect with the specifiedcollection ({collectionName})";
                error.Description = $"Failed to connect with the specified database ({database.DatabaseNamespace.DatabaseName}) because the server is either not up and running or the host ({this.Host}) is not reachable!";
                error.Code = -30013;
                this.HandleError(error);
                return null;
            }

            var matchFound = database.ListCollectionNames().ToList().Any(c => c == collectionName);

            if (!matchFound)
            {
                var error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.GetCollection));
                error.LogSource = LogSource.Connection;
                error.Message = $"Failed to connect with the collection ({collectionName})";
                error.Description = $"The specified collection ({collectionName}) is not found the specified database ({database.DatabaseNamespace.DatabaseName}) in the host {this.Host}!";
                error.Code = -30015;
                return null;
            }

            var coll = database.GetCollection<T>(collectionName);
            return coll;
        }

        public IResult<T> RunCommand<T>(string jsonCommand, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;

            if (!this.Connected)
            {
                error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.RunCommand));
                error.LogSource = LogSource.Connection;
                error.Description = $"Failed to connect with the specified database ({this.Database?.DatabaseNamespace?.DatabaseName}) because the server is either not up and running or the host ({this.Host}) is not reachable!";
                error.Code = -200;
                this.HandleError(error);
                return new Result<T>
                {
                    Success = false,
                    CommandStatus = CommandStatus.Terminated,
                    Errors = new List<ILogDetails> { error }
                };
            }

            try
            {
                var result = this.Database.RunCommand<T>(jsonCommand, null, cancellationToken);
                return new Result<T>
                {
                    Success = true,
                    Value = result,
                    CommandStatus = CommandStatus.Executed
                };
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                error.Code = -7000;
                error.SourceMethod = this.GetSourceName(nameof(this.RunCommand));
                error.LogSource = LogSource.Command;
                error.Description = $"Failed to execute the following command on the database ({this.Database.DatabaseNamespace.DatabaseName}) on the host ({this.Host}): {jsonCommand}!";
                this.HandleError(error);

                return new Result<T>
                {
                    Success = false,
                    CommandStatus = CommandStatus.ServerFailed,
                    Errors = new List<ILogDetails> { error }
                };
            }
        }

        public bool SetJsonSchemaValidation(string collection, string jsonSchema, ValidationLevel validationLevel, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;

            if (!this.Connected)
            {
                error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.SetJsonSchemaValidation));
                error.LogSource = LogSource.Connection;
                error.Message = "Failed to set the json schema validation!";
                error.Description = $"Failed to connect the host ({this.Host})!";
                error.Code = -2000;
                this.HandleError(error);
                return false;
            }

            if (this.Database is null)
            {
                error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.SetJsonSchemaValidation));
                error.LogSource = LogSource.Connection;
                error.Description = $"Failed to connect with the specified database ({this.connectionSettings?.Database}) on the host ({this.Host})!";
                error.Code = -250;
                this.HandleError(error);
                return false;
            }

            return this.SetJsonSchemaValidation(this.Database, collection, jsonSchema, validationLevel);
        }

        public bool SetJsonSchemaValidation(string database, string collection, string jsonSchema, ValidationLevel validationLevel, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;

            if (!this.Connected)
            {
                error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.SetJsonSchemaValidation));
                error.LogSource = LogSource.Connection;
                error.Message = "Failed to set the json schema validation!";
                error.Description = $"Failed to connect the host ({this.Host})!";
                error.Code = -2000;
                this.HandleError(error);
                return false;
            }


            var targetDatabase = this.GetDatabase(database);
            if (targetDatabase is null)
            {
                error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.SetJsonSchemaValidation));
                error.LogSource = LogSource.Connection;
                error.Description = $"Failed to connect with the specified database ({database}) on the host ({this.Host})!";
                error.Code = -255;
                this.HandleError(error);
                return false;
            }

            return this.SetJsonSchemaValidation(targetDatabase, collection, jsonSchema, validationLevel);
        }

        public bool SetJsonSchemaValidation(IMongoDatabase database, string collection, string jsonSchema, ValidationLevel validationLevel, CancellationToken cancellationToken = default)
        {
            ILogDetails error = null;

            if (!this.Connected)
            {
                error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.SetJsonSchemaValidation));
                error.LogSource = LogSource.Connection;
                error.Message = "Failed to set the json schema validation!";
                error.Description = $"Failed to connect the host ({this.Host})!";
                error.Code = -2000;
                this.HandleError(error);
                return false;
            }

            try
            {
                if (database is null)
                {
                    error = this.InitializeError();
                    error.SourceMethod = this.GetSourceName(nameof(this.SetJsonSchemaValidation));
                    error.LogSource = LogSource.Connection;
                    error.Description = $"Failed to connect with the specified database ({database}) on the host ({this.Host})!";
                    error.Code = -260;
                    this.HandleError(error);
                    return false;
                }

                var cmdJson = GetJsonSchemaValidationCommand(collection, jsonSchema, validationLevel);
                var result = database.RunCommand<dynamic>(cmdJson, null, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                error.Code = -2505;
                error.SourceMethod = this.GetSourceName(nameof(this.SetJsonSchemaValidation));
                error.LogSource = LogSource.Command;
                error.Description = $"Failed to set the json schema validation on the collection ({collection}) on the database ({database}) on the host ({this.Host}). The specified json schema: {jsonSchema}!";
                this.HandleError(error);
                return false;
            }
        }

        public bool DisableValidation(IMongoDatabase database, string collectionName)
        {
            var collection = this.GetCollection<dynamic>(database, collectionName);
            if (collection is null)
            {
                var error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.DisableValidation));
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to connect to the specified collection '{collectionName}' on the database ({database?.DatabaseNamespace?.DatabaseName}) on the host ({this.Host})!";
                error.Description = $"Failed to disable the validation for the collection ({collectionName}) on the database ({database?.DatabaseNamespace?.DatabaseName}) on the host ({this.Host})!";
                error.Code = -803;
                this.HandleError(error);
                return false;
            }

            try
            {
                var result = database.RunCommand<dynamic>($"{{collMod: '{collectionName}', validator: {{}}, validationLevel: 'off'}}");
                return true;
            }
            catch (Exception ex)
            {
                var error = this.InitializeError(ex);
                error.SourceMethod = this.GetSourceName(nameof(this.DisableValidation));
                error.LogSource = LogSource.Command;
                error.Description = $"Failed to disable the validation for the collection ({collectionName}) on the database ({database?.DatabaseNamespace?.DatabaseName}) on the host ({this.Host})!";
                error.Code = -801;
                this.HandleError(error);
                return false;
            }
        }

        public bool DisableValidation(IMongoDatabase database, IMongoCollection<dynamic> collection)
        {
            return this.DisableValidation(database, collection.CollectionNamespace.CollectionName);
        }

        public bool DisableValidation(string databaseName, string collectionName)
        {
            var database = this.GetDatabase(databaseName);
            if (database is null)
            {
                var error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.DisableValidation));
                error.LogSource = LogSource.Command;
                error.Message = $"Failed to connect to the specified database '{databaseName}' on the host ({this.Host})!";
                error.Description = $"Failed to disable the validation for the collection ({collectionName}) on the database ({database?.DatabaseNamespace?.DatabaseName}) on the host ({this.Host})!";
                error.Code = -802;
                this.HandleError(error);
                return false;
            }

            return this.DisableValidation(database, collectionName);
        }

        public CommandStatus ProvisionCollection(Collection collection)
        {
            return this.InitiateProvisioning(new List<Collection> { collection });
        }

        public CommandStatus ProvisionCollections(params Collection[] collections)
        {
            return this.InitiateProvisioning(collections);
        }

        public CommandStatus ProvisionCollections(ICollection<Collection> collections)
        {
            return this.InitiateProvisioning(collections);
        }
        #endregion Public Methods

        #region Private Methods
        private void InitiateConnection()
        {
            ILogDetails error = null;
            try
            {
                this.CollationSettings = this.connectionSettings.CollationSettings;
                var mcs = this.GetMongoClientSettings();
                this.client = new MongoClient(mcs);
                var server = this.client.Settings.Server;
                this.Host = $"{server.Host}:{server.Port}";
                this.Connected = true;
                if (!string.IsNullOrWhiteSpace(this.connectionSettings.Database))
                {
                    var database = this.GetDatabase(this.connectionSettings.Database);

                    if (this.Ping(database))
                    {
                        this.Database = database;
                    }
                }
            }
            catch (MongoClientException ex)
            {
                error = this.InitializeError(ex);
                error.SourceMethod = this.GetSourceName(nameof(this.InitiateConnection));
                error.LogSource = LogSource.Connection;
                error.Connected = false;
                error.Code = -100;
                error.Description = "Failed to connect to the host!";
                error.LogLevel = LogLevel.Critical;
            }
            catch (Exception ex)
            {
                error = this.InitializeError(ex);
                error.SourceMethod = this.GetSourceName(nameof(this.InitiateConnection));
                error.LogSource = LogSource.Connection;
                error.Connected = false;
                error.Code = -101;
                error.Description = "Failed to connect to the host!";
                error.LogLevel = LogLevel.Critical;
            }
            finally
            {
                this.HandleError(error);
            }
        }

        private CommandStatus InitiateProvisioning(ICollection<Collection> collectionsList)
        {
            if (!this.Connected)
            {
                return CommandStatus.Terminated;
            }

            var allSuccess = true;
            var partialSuccess = false;
            var collectionListInfos = this.GetCollectionListInfos();
            foreach (var coll in collectionsList)
            {
                var success = this.ProvisionCollection(coll, collectionListInfos);
                if (!success)
                {
                    allSuccess = false;
                }
                else
                {
                    partialSuccess = true;
                }
            }

            if (allSuccess)
            {
                this.logger?.LogInformation("All the specified collections have been successfully provisioned!", this.provisioningSettings);
                return CommandStatus.Completed;
            }
            else if (partialSuccess)
            {
                return CommandStatus.PartiallyCompleted;
            }
            else
            {
                return CommandStatus.ServerFailed;
            }
        }

        private bool ProvisionCollection(Collection coll, List<CollectionInfo> collectionListInfos)
        {
            ILogDetails error = null;
            var mongoDatabase = this.GetDatabase(coll.DatabaseName);
            if (mongoDatabase is null)
            {
                error = this.InitializeError();
                error.SourceMethod = this.GetSourceName(nameof(this.ProvisionCollection));
                error.LogSource = LogSource.Connection;
                error.Description = $"Failed to access the specified database ({coll.DatabaseName})!";
                error.Message = $"Failed to provision the collection ({coll.CollectionName}) in the database ({coll.DatabaseName}) in the host ({this.Host})!";
                error.Code = -601;
                this.HandleError(error);
                return false;
            }

            var mongoCollection = this.GetCollection<dynamic>(mongoDatabase, coll.CollectionName);
            if (mongoCollection is null)
            {
                try
                {
                    var collation = coll.Collation.ToMongoCollation();

                    var collectionOption = new CreateCollectionOptions<dynamic>
                    {
                        Collation = collation,
                        ValidationLevel = DocumentValidationLevel.Strict,
                        ValidationAction = DocumentValidationAction.Error
                    };

                    if (coll.SchemaValidation is not null && !string.IsNullOrWhiteSpace(coll.SchemaValidation.JsonSchemaContents))
                    {
                        var validator = new JsonFilterDefinition<dynamic>(coll.SchemaValidation.JsonSchemaContents);
                        collectionOption.Validator = validator;
                        collectionOption.ValidationLevel = (DocumentValidationLevel)coll.SchemaValidation.ValidationLevel;
                    }

                    mongoDatabase.CreateCollection(coll.CollectionName, collectionOption);
                    mongoCollection = this.GetCollection<dynamic>(mongoDatabase, coll.CollectionName);
                    if (mongoCollection is null)
                    {
                        error = this.InitializeError();
                        error.SourceMethod = this.GetSourceName(nameof(this.ProvisionCollection));
                        error.LogSource = LogSource.Connection;
                        error.Description = $"Failed to create/access specified the collection ({coll.CollectionName})!";
                        error.Message = $"Failed to provision the collection ({coll.CollectionName}) in the database ({coll.DatabaseName}) in the host ({this.Host})!";
                        error.Code = -602;
                        this.HandleError(error);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = this.InitializeError(ex);
                    error.SourceMethod = this.GetSourceName(nameof(this.ProvisionCollection));
                    error.LogSource = LogSource.Connection;
                    error.Description = $"Failed to provision the collection ({coll.CollectionName}) in the database ({coll.DatabaseName}) in the host ({this.Host})!";
                    error.Code = -603;
                    this.HandleError(error);
                    return false;
                }
            }
            else
            {
                coll.Exists = true;
            }

            coll.MongoCollection = mongoCollection;
            var schemaProvisioned = true;
            if (coll.Exists && coll.SchemaValidation is not null)
            {
                if (collectionListInfos.IsNotNullOrEmpty<CollectionInfo>())
                {
                    var collInfo = collectionListInfos.FirstOrDefault(c => c.Name == coll.CollectionName);
                    if (coll.SchemaValidation.ClearValidation)
                    {
                        if (collInfo.HasValidator)
                        {
                            schemaProvisioned = this.DisableValidation(coll.MongoCollection.Database, coll.CollectionName);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(coll.SchemaValidation.JsonSchemaContents))
                    {
                        if (!collInfo.HasValidator || (collInfo.HasValidator && coll.SchemaValidation.OverrideValidation))
                        {
                            schemaProvisioned = this.SetJsonSchemaValidation(coll.CollectionName, coll.SchemaValidation.JsonSchemaContents, coll.SchemaValidation.ValidationLevel);
                        }
                    }
                }

                if (schemaProvisioned)
                {
                    this.logger?.LogInformation("The schema validation of the following collection has been provisioned successfully!", coll);
                }
                else
                {
                    error = this.InitializeError();
                    error.SourceMethod = this.GetSourceName(nameof(this.ProvisionCollection));
                    error.LogSource = LogSource.Connection;
                    error.Description = $"Failed to provision the following json schema validation: {coll.SchemaValidation.JsonSchemaContents}";
                    error.Message = $"Failed to fullly provision the collection ({coll.CollectionName}) in the database ({coll.DatabaseName}) in the host ({this.Host})!";
                    error.Code = -625;
                    this.HandleError(error);
                }
            }

            if (coll.Indexes.IsNullOrEmpty())
            {
                return true;
            }

            var fullyProvisioned = schemaProvisioned && this.ProvisionCollectionIndexes(mongoCollection, coll);
            if (fullyProvisioned)
            {
                this.logger?.LogInformation("The following collection has been created successfully and fully provisioned!", coll);
            }
            else
            {
                this.logger?.LogWarning("The following collection has been created successfully but not fully provisioned!", coll);
            }

            return fullyProvisioned;
        }

        private bool ProvisionCollectionIndexes(IMongoCollection<dynamic> mongoCollection, Collection provCollection)
        {
            var allSuccess = true;
            var existingIndexes = mongoCollection.Indexes.List().ToList().Select(i => i.GetElement("name").Value.ToString()).OfType<string>().ToList();
            if (existingIndexes.Any() && provCollection.DropExistingIndexes)
            {
                try
                {
                    mongoCollection.Indexes.DropAll();
                }
                catch (Exception ex)
                {
                    var error = this.InitializeError(ex);
                    error.SourceMethod = this.GetSourceName(nameof(this.ProvisionCollectionIndexes));
                    error.LogSource = LogSource.Connection;
                    error.Description = $"Failed to drop all the indexes!";
                    error.Message = $"Failed to provision the indexes in the collection ({mongoCollection.CollectionNamespace.FullName}) in the host ({this.Host})!";
                    error.Code = -604;
                    this.HandleError(error);
                    return false;
                }
            }

            foreach (var index in provCollection.Indexes)
            {
                if (!provCollection.DropExistingIndexes && existingIndexes.Contains(index.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (index.DropExisting)
                    {
                        try
                        {
                            mongoCollection.Indexes.DropOne(index.Name);
                            this.logger?.LogInformation("The following index has been dropped successfully!", index);
                            if (index.Fields.IsNullOrEmpty())
                            {
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            var error = this.InitializeError(ex);
                            error.SourceMethod = this.GetSourceName(nameof(this.ProvisionCollectionIndexes));
                            error.LogSource = LogSource.Connection;
                            error.Description = $"Failed to drop the index ({index.Name})!";
                            error.Message = $"Failed to provision the indexes the collection ({mongoCollection.CollectionNamespace.FullName}) in the host ({this.Host})!";
                            error.Code = -605;
                            this.HandleError(error);
                            allSuccess = false;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                var ikdGroup = new List<IndexKeysDefinition<dynamic>>();
                foreach (var field in index.Fields)
                {
                    switch (field.IndexType)
                    {
                        case IndexFieldType.Desc:
                            {
                                ikdGroup.Add(Builders<dynamic>.IndexKeys.Descending(field.FieldName));
                                break;
                            }

                        case IndexFieldType.Text:
                            {
                                ikdGroup.Add(Builders<dynamic>.IndexKeys.Text(field.FieldName));
                                break;
                            }

                        case IndexFieldType.Geo2DSphere:
                            {
                                ikdGroup.Add(Builders<dynamic>.IndexKeys.Geo2DSphere(field.FieldName));
                                break;
                            }

                        case IndexFieldType.Geo2D:
                            {
                                ikdGroup.Add(Builders<dynamic>.IndexKeys.Geo2DSphere(field.FieldName));
                                break;
                            }

                        default:
                            {
                                ikdGroup.Add(Builders<dynamic>.IndexKeys.Ascending(field.FieldName));
                                break;
                            }

                    }
                }

                var compundIndex = Builders<dynamic>.IndexKeys.Combine(ikdGroup);
                var indexOption = new CreateIndexOptions
                {
                    Name = index.Name,
                    Collation = index.CollationOptions.ToMongoCollation(),
                    Unique = index.Unique
                };

                var indexModel = new CreateIndexModel<dynamic>(compundIndex, indexOption);
                try
                {
                    var indexCreated = mongoCollection.Indexes.CreateOne(indexModel);
                    this.logger?.LogInformation("The following index has been created successfully!", index);
                }
                catch (Exception ex)
                {
                    var error = this.InitializeError(ex);
                    error.SourceMethod = this.GetSourceName(nameof(this.ProvisionCollectionIndexes));
                    error.LogSource = LogSource.Connection;
                    error.Description = $"Failed to create the index ({index.Name})!";
                    error.Message = $"Failed to provision the indexes the collection ({mongoCollection.CollectionNamespace.FullName}) in the host ({this.Host})!";
                    error.Code = -610;
                    this.HandleError(error);
                    allSuccess = false;
                }
            }

            return allSuccess;
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
            var error = new LogDetails(
                this.Host,
                this.connectionSettings?.Database,
                null,
                this.CorrelationId,
                this.StartTime,
                this.Connected,
                null,
                ex)
            {
                LogLevel = LogLevel.Critical
            };

            return error;
        }

        private MongoClientSettings GetMongoClientSettings()
        {
            var mcs = MongoClientSettings.FromConnectionString(this.connectionSettings.ConnectionString);
            return mcs;
        }

        private string GetSourceName(string methodName)
        {
            return $"{this.fullTypeName}.{methodName}";
        }

        private static string GetJsonSchemaValidationCommand(string collection, string jsonSchema, ValidationLevel validationLevel)
        {
            var cmdJson = $"{{collMod: '{collection}', validator:{jsonSchema}, 'validationLevel': '{validationLevel.ToString().ToLowerInvariant()}'}}";
            return cmdJson;
        }

        private List<CollectionInfo> GetCollectionListInfos()
        {
            try
            {
                var listCollsDynamic = this.Database.RunCommand<dynamic>("{ listCollections: 1.0 }");
                var listColls = listCollsDynamic.cursor.firstBatch;
                var json = System.Text.Json.JsonSerializer.Serialize(listColls);
                var collectionInfoList = System.Text.Json.JsonSerializer.Deserialize<List<CollectionInfo>>(json);
                return collectionInfoList;
            }
            catch
            {
                return null;
            }
        }
        #endregion Private Methods
    }
}