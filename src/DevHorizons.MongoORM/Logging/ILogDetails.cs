namespace DevHorizons.MongoORM.Logging
{
    using Microsoft.Extensions.Logging;

    public interface ILogDetails
    {
         string Host { get; set; }

         string Database { get; set; }

         string Collection { get; set; }

         bool Connected { get; set; }

         LogSource LogSource { get; set; }

         int Code { get; set; }

         string Message { get; set; }

         string Description { get; set; }

         string SourceMethod { get; set; }

         string StackTrace { get; set; }

         Exception Exception { get; set; }

         Guid? ConnectionCorrelationId { get; set; }

         Guid? CollectionCorrelationId { get; set; }

         DateTime? ConnectionStartedTime { get; set; }

         DateTime Created { get; set; }

         LogLevel LogLevel { get; set; }
    }
}
