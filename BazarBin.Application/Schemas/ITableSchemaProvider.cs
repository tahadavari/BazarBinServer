using BazarBin.Domain.Schemas;

namespace BazarBin.Application.Schemas;

public interface ITableSchemaProvider
{
    Task<TableSchema?> GetSchemaAsync(string datasetId, CancellationToken cancellationToken);
}
