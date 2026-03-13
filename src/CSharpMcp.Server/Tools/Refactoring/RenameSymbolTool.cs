using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Refactoring;

[McpServerToolType]
public class RenameSymbolTool
{
    [McpServerTool, Description("Rename a symbol (variable, method, class, etc.) across the entire workspace. Returns the number of files and references affected.")]
    public static async Task<string> RenameSymbol(
        IWorkspaceManager workspaceManager,
        ILogger<RenameSymbolTool> logger,
        CancellationToken cancellationToken,
        [Description("The new name for the symbol")] string newName,
        [Description("The name of the symbol to rename")] string symbolName = "",
        [Description("Path to the file containing the symbol")] string filePath = "",
        [Description("1-based line number near the symbol declaration")] int lineNumber = 0)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Rename Symbol");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            // Validate new name
            if (string.IsNullOrWhiteSpace(newName))
            {
                return GetErrorHelpResponse("New name cannot be empty.");
            }

            // 确保工作区是最新的（如果需要会重新加载整个工作区）
            await workspaceManager.EnsureRefreshAsync(cancellationToken);

            logger.LogInformation("Renaming symbol: {FilePath}:{LineNumber} - {SymbolName} -> {NewName}",
                filePath, lineNumber, symbolName, newName);

            // Resolve the symbol
            var symbol = await SymbolResolver.ResolveSymbolAsync(
                filePath, lineNumber, symbolName ?? "",
                workspaceManager,
                SymbolFilter.TypeAndMember | SymbolFilter.Namespace,
                cancellationToken);

            if (symbol == null)
            {
                var errorDetails = await MarkdownHelper.BuildSymbolNotFoundErrorDetailsAsync(
                    filePath, lineNumber, symbolName ?? "Not specified",
                    workspaceManager.GetCurrentSolution(), cancellationToken);
                return GetErrorHelpResponse($"Symbol not found.\n\n{errorDetails}");
            }

            // Check if the symbol can be renamed
            if (symbol.Locations.Length > 0 && symbol.Locations[0].IsInMetadata)
            {
                return GetErrorHelpResponse($"Cannot rename symbol '{symbol.Name}': it is defined in referenced metadata (external assembly).");
            }

            var solution = workspaceManager.GetCurrentSolution();
            if (solution == null)
            {
                return GetErrorHelpResponse("Solution not available.");
            }

            // Get all references before rename
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
            var refList = references.ToList();
            var totalRefs = refList.Sum(r => r.Locations.Count());
            var affectedFiles = refList
                .SelectMany(r => r.Locations)
                .Select(l => l.Document.FilePath)
                .Distinct()
                .Count();

            // Perform the rename
            var newSolution = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                newName,
                solution.Options,
                cancellationToken);

            // Apply changes to workspace
            var workspace = workspaceManager.GetCurrentSolution()?.Workspace;
            if (workspace == null)
            {
                return GetErrorHelpResponse("Workspace not available for applying changes.");
            }

            var applied = workspace.TryApplyChanges(newSolution);
            if (!applied)
            {
                return GetErrorHelpResponse("Failed to apply rename changes to workspace.");
            }

            logger.LogInformation("Successfully renamed '{OldName}' to '{NewName}' across {Count} files",
                symbol.Name, newName, affectedFiles);

            return BuildSuccessResponse(symbol, newName, affectedFiles, totalRefs);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error executing RenameSymbolTool");
            return GetErrorHelpResponse($"Failed to rename symbol: {ex.Message}");
        }
    }

    private static string BuildSuccessResponse(ISymbol symbol, string newName, int affectedFiles, int totalRefs)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## Renamed: `{symbol.Name}` → `{newName}`");
        sb.AppendLine();

        sb.AppendLine($"- **Symbol**: `{symbol.GetDisplayName()}`");
        sb.AppendLine($"- **Kind**: {symbol.Kind}");
        sb.AppendLine($"- **References Updated**: {totalRefs}");
        sb.AppendLine($"- **Files Modified**: {affectedFiles}");

        return sb.ToString();
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Rename Symbol",
            message,
            "RenameSymbol(\n    newName: \"NewMethodName\",\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42,\n    symbolName: \"OldMethodName\"\n)",
            "- `RenameSymbol(newName: \"ProcessData\", filePath: \"C:/MyProject/Service.cs\", lineNumber: 15, symbolName: \"DoWork\")`\n- `RenameSymbol(newName: \"_logger\", filePath: \"./MyClass.cs\", lineNumber: 10, symbolName: \"Logger\", previewOnly: true)`"
        );
    }
}
