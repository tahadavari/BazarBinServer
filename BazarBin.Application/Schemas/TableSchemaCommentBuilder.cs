using System.Text;
using BazarBin.Domain.Schemas;

namespace BazarBin.Application.Schemas;

public sealed class TableSchemaCommentBuilder : ITableSchemaCommentBuilder
{
    public string BuildComment(TableSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var builder = new StringBuilder();
        builder.AppendLine("/* Table Schema");
        foreach (var table in schema.Tables)
        {
            builder.Append(" * Table: ").Append(table.Name);
            if (!string.IsNullOrWhiteSpace(table.Description))
            {
                builder.Append(" - ").Append(table.Description);
            }

            builder.AppendLine();
            foreach (var column in table.Columns)
            {
                builder.Append(" *   - ")
                    .Append(column.Name)
                    .Append(": ")
                    .Append(column.DataType);
                if (!string.IsNullOrWhiteSpace(column.Description))
                {
                    builder.Append(" (" + column.Description + ")");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine(" */");
        return builder.ToString().TrimEnd();
    }
}
