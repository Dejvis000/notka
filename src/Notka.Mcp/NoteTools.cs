using System.ComponentModel;
using ModelContextProtocol.Server;
using Notka.Core;

namespace Notka.Mcp
{
    [McpServerToolType]
    public static class NoteTools
    {
        [McpServerTool(Name = "create_note"), Description(
            "Creates a new sticky note in the user's Notka app and returns it as JSON. " +
            "Use it for free-form text the user wants to jot down, as opposed to a task with a deadline.")]
        public static async Task<string> CreateNote(
            NotkaApiClient api,
            [Description("Text of the note, up to 10000 characters.")] string content,
            [Description("Note color name, up to 32 characters, e.g. yellow (default), blue, green, pink.")] string color = "yellow",
            CancellationToken cancellationToken = default) =>
            ToolInput.ToJson(await api.CreateNoteAsync(new NoteRequest { Content = content, Color = color }, cancellationToken));

        [McpServerTool(Name = "list_notes", ReadOnly = true), Description(
            "Lists all sticky notes from the user's Notka app as a JSON array, pinned notes first, then the most recently changed.")]
        public static async Task<string> ListNotes(NotkaApiClient api, CancellationToken cancellationToken = default) =>
            ToolInput.ToJson(await api.ListNotesAsync(cancellationToken));
    }
}
