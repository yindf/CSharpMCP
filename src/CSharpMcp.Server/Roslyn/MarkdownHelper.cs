using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// Helper methods for common markdown formatting operations
/// </summary>
public static class MarkdownHelper
{
    /// <summary>
    /// Extract line text from a document
    /// </summary>
    public static async Task<string?> ExtractLineTextAsync(
        Document document,
        int lineNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceText = await document.GetTextAsync(cancellationToken);
            var lines = sourceText.Lines;

            if (lineNumber < 1 || lineNumber > lines.Count)
                return null;

            var lineIndex = lineNumber - 1;
            return lines[lineIndex].ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Build a formatted error response header
    /// </summary>
    public static StringBuilder BuildErrorHeader(string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {title} - Failed");
        sb.AppendLine();
        return sb;
    }

    /// <summary>
    /// Build a formatted error message with details
    /// </summary>
    public static string BuildErrorResponse(string title, string message, string? usage = null, string? examples = null)
    {
        var sb = BuildErrorHeader(title);
        sb.AppendLine(message);
        sb.AppendLine();

        if (usage != null)
        {
            sb.AppendLine("**Usage:**");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(usage);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (examples != null)
        {
            sb.AppendLine("**Examples:**");
            sb.AppendLine(examples);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format file location as markdown
    /// </summary>
    public static string FormatFileLocation(string filePath, int startLine, int endLine)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        return $"`{fileName}:{startLine}-{endLine}`";
    }

    /// <summary>
    /// Format file location as markdown (single line)
    /// </summary>
    public static string FormatFileLocation(string filePath, int lineNumber)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        return $"`{fileName}:{lineNumber}`";
    }

    /// <summary>
    /// Get plural form of a count
    /// </summary>
    public static string Pluralize(int count, string singular, string? plural = null)
    {
        plural ??= singular + "s";
        return count == 1 ? singular : plural;
    }
}
