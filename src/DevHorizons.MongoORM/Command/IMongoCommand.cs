namespace DevHorizons.MongoORM.Command
{
    using System.Linq.Expressions;

    using MongoDB.Driver;

    using Result;

    public interface IMongoCommand<T> : IMongoCommandBase
    {
        #region Public Methods
        #region DAO Methods
        #region Aggregation
        IResult<long> GetTotalCount();

        Task<IResult<long>> GetTotalCountAsync();

        IResult<long> GetCount(Expression<Func<T, bool>> filter);

        Task<IResult<long>> GetCountAsync(Expression<Func<T, bool>> filter);

        IResult<long> GetCount(FilterDefinition<T> filter);

        Task<IResult<long>> GetCountAsync(FilterDefinition<T> filter);
        #endregion Aggregation

        #region Get
        #region Get All
        IResult<ICollection<T>> GetAll(int pageSize = 0, int page = 0);

        Task<IResult<ICollection<T>>> GetAllAsync(int pageSize = 0, int page = 0);
        #endregion Get All

        #region Get Filter
        IResult<ICollection<T>> Get(string filter, int pageSize = 0, int page = 0);

        Task<IResult<ICollection<T>>> GetAsync(string filter, int pageSize = 0, int page = 0);

        IResult<ICollection<T>> Get(Expression<Func<T, bool>> filter, int pageSize = 0, int page = 0);

        Task<IResult<ICollection<T>>> GetAsync(Expression<Func<T, bool>> filter, int pageSize = 0, int page = 0);
        #endregion Get Filter

        #region Get By Unique ID
        IResult<T> Get(Guid id);

        Task<IResult<T>> GetAsync(Guid id);
        #endregion Get By Unique ID

        #region Get From Range
        IResult<ICollection<T>> Get<TField>(Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0);

        Task<IResult<ICollection<T>>> GetAsync<TField>(Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0);

        IResult<ICollection<T>> Get<TField>(FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0);

        Task<IResult<ICollection<T>>> GetAsync<TField>(FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0);

        IResult<ICollection<T>> Get<TField>(Expression<Func<T, bool>> filter, Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0);

        Task<IResult<ICollection<T>>> GetAsync<TField>(Expression<Func<T, bool>> filter, Expression<Func<T, TField>> field, IEnumerable<TField> values, int pageSize = 0, int page = 0);

        IResult<ICollection<T>> Get<TField>(Expression<Func<T, bool>> filter, FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0);

        Task<IResult<ICollection<T>>> GetAsync<TField>(Expression<Func<T, bool>> filter, FieldDefinition<T, TField> field, IEnumerable<TField> values, int pageSize = 0, int page = 0);
        #endregion Get From Range
        #endregion Get

        #region Add
        IResult<bool> Add(T document, CancellationToken cancellationToken = default);

        Task<IResult<bool>> AddAsync(T document, CancellationToken cancellationToken = default);

        IResult<bool> AddMany(ICollection<T> documents, CancellationToken cancellationToken = default);

        Task<IResult<bool>> AddManyAsync(ICollection<T> documents, CancellationToken cancellationToken = default);
        #endregion Add

        #region Update
        #region Update Document
        IResult<bool> Update(Guid id, T document, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync(Guid id, T document, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(Expression<Func<T, TField>> field, TField value, T document, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, TField>> field, TField value, T document, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(FieldDefinition<T, TField> field, TField value, T document, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(FieldDefinition<T, TField> field, TField value, T document, CancellationToken cancellationToken = default);
        #endregion Update Document

        #region Update One Field
        #region By ID
        IResult<bool> Update<TField>(Guid id, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(Guid id, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(Guid id, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(Guid id, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default);
        #endregion By ID

        #region By Filter
        IResult<bool> Update<TField>(Expression<Func<T, bool>> filter, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, bool>> filter, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(Expression<Func<T, bool>> filter, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, bool>> filter, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(FilterDefinition<T> filter, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(FilterDefinition<T> filter, Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(FilterDefinition<T> filter, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(FilterDefinition<T> filter, FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default);

        #endregion By Filter
        #endregion Update One Field

        #region Update Multiple Fields
        #region By ID
        IResult<bool> Update<TField>(Guid id, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(Guid id, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(Guid id, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(Guid id, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default);

        IResult<bool> Update(Guid id, IDictionary<string, object> pairs, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync(Guid id, IDictionary<string, object> pairs, CancellationToken cancellationToken = default);
        #endregion By ID

        #region By Filter
        IResult<bool> Update<TField>(Expression<Func<T, bool>> filter, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, bool>> filter, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(Expression<Func<T, bool>> filter, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(Expression<Func<T, bool>> filter, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default);

        IResult<bool> Update(Expression<Func<T, bool>> filter, IDictionary<string, object> pairs, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync(Expression<Func<T, bool>> filter, IDictionary<string, object> pairs, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(FilterDefinition<T> filter, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(FilterDefinition<T> filter, IDictionary<Expression<Func<T, TField>>, TField> pairs, CancellationToken cancellationToken = default);

        IResult<bool> Update<TField>(FilterDefinition<T> filter, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync<TField>(FilterDefinition<T> filter, IDictionary<FieldDefinition<T, TField>, TField> pairs, CancellationToken cancellationToken = default);

        IResult<bool> Update(FilterDefinition<T> filter, IDictionary<string, object> pairs, CancellationToken cancellationToken = default);

        Task<IResult<bool>> UpdateAsync(FilterDefinition<T> filter, IDictionary<string, object> pairs, CancellationToken cancellationToken = default);
        #endregion By Filter
        #endregion Update Multiple Fields
        #endregion Update

        #region Delete
        #region Delete Single
        IResult<bool> Delete(Guid id, CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        IResult<bool> Delete<TField>(Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteAsync<TField>(Expression<Func<T, TField>> field, TField value, CancellationToken cancellationToken = default);

        IResult<bool> Delete<TField>(FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteAsync<TField>(FieldDefinition<T, TField> field, TField value, CancellationToken cancellationToken = default);
        #endregion Delete Single

        #region Delete Many
        #region Delete Many By Filter
        IResult<bool> DeleteMany(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteManyAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default);

        IResult<bool> DeleteMany(FilterDefinition<T> filter, CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default);
        #endregion Delete Many By Filter

        #region Delete Many By Filter & Range Field/Value
        IResult<bool> DeleteByRange<TField>(Expression<Func<T, bool>> primaryFilter, Expression<Func<T, TField>> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteByRangeAsync<TField>(Expression<Func<T, bool>> primaryFilter, Expression<Func<T, TField>> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default);

        IResult<bool> DeleteByRange<TField>(Expression<Func<T, bool>> primaryFilter, FieldDefinition<T, TField> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteByRangeAsync<TField>(Expression<Func<T, bool>> primaryFilter, FieldDefinition<T, TField> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default);

        IResult<bool> DeleteByRange<TField>(FilterDefinition<T> primaryFilter, Expression<Func<T, TField>> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteByRangeAsync<TField>(FilterDefinition<T> primaryFilter, Expression<Func<T, TField>> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default);

        IResult<bool> DeleteByRange<TField>(FilterDefinition<T> primaryFilter, FieldDefinition<T, TField> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteByRangeAsync<TField>(FilterDefinition<T> primaryFilter, FieldDefinition<T, TField> field, IEnumerable<TField> rangeValues, CancellationToken cancellationToken = default);
        #endregion Delete Many By Filter & Range Field/Value

        #region Delete All
        IResult<bool> DeleteAll(CancellationToken cancellationToken = default);

        Task<IResult<bool>> DeleteAllAsync(CancellationToken cancellationToken = default);
        #endregion Delete All
        #endregion Delete Many
        #endregion Delete
        #endregion DAO Methods
        #endregion Public Methods
    }
}
