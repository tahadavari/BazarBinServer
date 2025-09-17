using BazarBin.Domain.Schemas;

namespace BazarBin.Application.Schemas;

public interface ITableSchemaCommentBuilder
{
    string BuildComment(TableSchema schema);
}
