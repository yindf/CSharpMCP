using System.Linq;
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

    /// <summary>
    /// Build detailed error information when a symbol is not found
    /// </summary>
    public static async Task<StringBuilder> BuildSymbolNotFoundErrorDetailsAsync(
        string filePath,
        int lineNumber,
        string symbolName,
        Solution? solution,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Symbol Not Found");
        sb.AppendLine();
        sb.AppendLine($"**File**: `{filePath}`");
        sb.AppendLine($"**Line Number**: {lineNumber}");
        sb.AppendLine($"**Symbol Name**: `{symbolName}`");
        sb.AppendLine();

        // Try to read file content for context
        var lineContent = await TryExtractLineContentAsync(filePath, lineNumber, solution, cancellationToken);
        if (lineContent != null)
        {
            sb.AppendLine("**Line Content**:");
            sb.AppendLine("```csharp");
            sb.AppendLine(lineContent);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("**Possible Reasons**:");
        sb.AppendLine("1. The symbol is defined in an external library (not in this workspace)");
        sb.AppendLine("2. The symbol is a built-in C# type or keyword");
        sb.AppendLine("3. The file path or line number is incorrect");
        sb.AppendLine("4. The workspace needs to be reloaded (try LoadWorkspace again)");

        return sb;
    }

    /// <summary>
    /// Try to extract line content from a document
    /// </summary>
    private static async Task<string?> TryExtractLineContentAsync(
        string filePath,
        int lineNumber,
        Solution? solution,
        CancellationToken cancellationToken)
    {
        if (solution == null || lineNumber <= 0)
        {
            return null;
        }

        try
        {
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath);

            if (document == null)
            {
                return null;
            }

            var sourceText = await document.GetTextAsync(cancellationToken);
            if (sourceText == null)
            {
                return null;
            }

            var lineIndex = lineNumber - 1;
            if (lineIndex < 0 || lineIndex >= sourceText.Lines.Count)
            {
                return null;
            }

            return sourceText.Lines[lineIndex].ToString().Trim();
        }
        catch
        {
            return null;
        }
    }
}
