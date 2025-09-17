using BazarBin.Application.Schemas;
using BazarBin.Domain.Schemas;
using FluentAssertions;
using Xunit;

namespace BazarBin.Tests.Application.Schemas;

public sealed class TableSchemaCommentBuilderTests
{
    [Fact]
    public void BuildComment_ReturnsFormattedSchema()
    {
        var schema = new TableSchema(
            "sales",
            new[]
            {
                new TableDefinition(
                    "orders",
                    "Order header data",
                    new[]
                    {
                        new ColumnDefinition("order_id", "uuid", "Primary key"),
                        new ColumnDefinition("total", "decimal", null!)
                    }),
                new TableDefinition(
                    "order_items",
                    null,
                    new[]
                    {
                        new ColumnDefinition("order_id", "uuid", "Foreign key"),
                        new ColumnDefinition("sku", "text", "Item sku")
                    })
            });

        var builder = new TableSchemaCommentBuilder();

        var comment = builder.BuildComment(schema);

        comment.Should().StartWith("/* Table Schema");
        comment.Should().Contain("Table: orders - Order header data");
        comment.Should().Contain("- order_id: uuid (Primary key)");
        comment.Should().Contain("- total: decimal");
        comment.Should().Contain("Table: order_items");
        comment.Should().Contain("- sku: text (Item sku)");
        comment.Should().EndWith("*/");
    }

    [Fact]
    public void BuildComment_WhenSchemaIsNull_Throws()
    {
        var builder = new TableSchemaCommentBuilder();

        var act = () => builder.BuildComment(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
