using System.Reflection;
using BazarBin.Services;
using Xunit;

namespace BazarBin.Tests;

public class CsvImportServiceTests
{
    private static string InvokeTableCommentBuilder(string qualifiedTableName, string comment)
    {
        var method = typeof(CsvImportService).GetMethod(
            "BuildTableCommentSql",
            BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidOperationException("BuildTableCommentSql not found");

        return (string)(method.Invoke(null, new object[] { qualifiedTableName, comment })
                         ?? throw new InvalidOperationException("BuildTableCommentSql returned null"));
    }

    private static string InvokeColumnCommentBuilder(string qualifiedTableName, string columnName, string comment)
    {
        var method = typeof(CsvImportService).GetMethod(
            "BuildColumnCommentSql",
            BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidOperationException("BuildColumnCommentSql not found");

        return (string)(method.Invoke(null, new object[] { qualifiedTableName, columnName, comment })
                         ?? throw new InvalidOperationException("BuildColumnCommentSql returned null"));
    }

    [Fact]
    public void BuildTableCommentSql_ProducesLiteral()
    {
        var sql = InvokeTableCommentBuilder("\"s\".\"t\"", "hello world");
        Assert.Equal("COMMENT ON TABLE \"s\".\"t\" IS 'hello world';", sql);
    }

    [Fact]
    public void BuildTableCommentSql_EscapesSingleQuotes()
    {
        var sql = InvokeTableCommentBuilder("\"s\".\"t\"", "O'Reilly");
        Assert.Equal("COMMENT ON TABLE \"s\".\"t\" IS 'O''Reilly';", sql);
    }

    [Fact]
    public void BuildColumnCommentSql_ProducesLiteral()
    {
        var sql = InvokeColumnCommentBuilder("\"s\".\"t\"", "col", "value");
        Assert.Equal("COMMENT ON COLUMN \"s\".\"t\".\"col\" IS 'value';", sql);
    }
}
