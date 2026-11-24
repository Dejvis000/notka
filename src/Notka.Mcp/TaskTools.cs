using System.ComponentModel;
using ModelContextProtocol.Server;
using Notka.Core;

namespace Notka.Mcp
{
    [McpServerToolType]
    public static class TaskTools
    {
        [McpServerTool(Name = "add_task"), Description(
            "Creates a new task in the user's Notka app and returns it as JSON, including its numeric id. " +
            "Use it when the user asks to add, remember or schedule something to do.")]
        public static async Task<string> AddTask(
            NotkaApiClient api,
            [Description("Short task title, up to 500 characters.")] string title,
            [Description("Optional longer description. Omit if there is nothing to add.")] string? notes = null,
            [Description("Optional due date and time in the user's LOCAL time, ISO 8601 without time zone, e.g. 2026-11-25T10:00:00. Resolve relative phrases like 'tomorrow at 10' to an absolute date first. Omit for a task without a deadline.")] string? dueAt = null,
            [Description("Priority: Low, Normal (default) or High.")] string? priority = null,
            [Description("Optional tags, each up to 50 characters, at most 20 tags.")] string[]? tags = null,
            CancellationToken cancellationToken = default)
        {
            var request = new TaskRequest
            {
                Title = title,
                Notes = notes,
                DueAt = ToolInput.ParseDueAt(dueAt),
                Priority = ToolInput.ParsePriority(priority),
                Tags = tags?.ToList(),
            };
            return ToolInput.ToJson(await api.CreateTaskAsync(request, cancellationToken));
        }

        [McpServerTool(Name = "list_tasks", ReadOnly = true), Description(
            "Lists tasks from the user's Notka app as a JSON array, sorted by due date (tasks without a date last), then priority. " +
            "Use it to answer what the user has to do today, what is overdue or coming up.")]
        public static async Task<string> ListTasks(
            NotkaApiClient api,
            [Description("Which tasks to return: Today (not done, due today), Overdue (not done, due before today), Upcoming (not done, due from tomorrow), Done (completed) or All (everything, the default). Matching ignores case. Tasks without a due date appear only in All, and in Done once completed.")] string filter = "All",
            CancellationToken cancellationToken = default) =>
            ToolInput.ToJson(await api.ListTasksAsync(filter, cancellationToken));

        [McpServerTool(Name = "search_tasks", ReadOnly = true), Description(
            "Searches task titles and notes for a text fragment and returns matching tasks as a JSON array. " +
            "Use it to find a task, for example to get its id before completing or updating it. " +
            "Letter case is ignored only for ASCII characters, so Polish diacritics must match exactly.")]
        public static async Task<string> SearchTasks(
            NotkaApiClient api,
            [Description("Text fragment to look for, must not be empty.")] string query,
            CancellationToken cancellationToken = default) =>
            ToolInput.ToJson(await api.SearchTasksAsync(query, cancellationToken));

        [McpServerTool(Name = "complete_task", Idempotent = true), Description(
            "Marks a task as done and returns it as JSON. Completing an already completed task changes nothing. " +
            "If you do not know the id, call search_tasks or list_tasks first.")]
        public static async Task<string> CompleteTask(
            NotkaApiClient api,
            [Description("Numeric id of the task, as returned by the other task tools.")] int id,
            CancellationToken cancellationToken = default) =>
            ToolInput.ToJson(await api.CompleteTaskAsync(id, cancellationToken));

        [McpServerTool(Name = "update_task", Idempotent = true), Description(
            "Changes an existing task and returns it as JSON. Only the parameters you pass are changed, the rest stays as it was. " +
            "If you do not know the id, call search_tasks or list_tasks first.")]
        public static async Task<string> UpdateTask(
            NotkaApiClient api,
            [Description("Numeric id of the task to change.")] int id,
            [Description("New title, up to 500 characters.")] string? title = null,
            [Description("New notes. Pass an empty string to remove the notes.")] string? notes = null,
            [Description("New due date and time in the user's LOCAL time, ISO 8601 without time zone, e.g. 2026-11-25T10:00:00.")] string? dueAt = null,
            [Description("Set to true to remove the due date. Ignored when dueAt is passed.")] bool clearDueAt = false,
            [Description("New priority: Low, Normal or High.")] string? priority = null,
            [Description("New complete list of tags, replacing the old ones. Pass an empty array to remove all tags.")] string[]? tags = null,
            CancellationToken cancellationToken = default)
        {
            var current = await api.GetTaskAsync(id, cancellationToken);
            var newDueAt = ToolInput.ParseDueAt(dueAt);
            var request = new TaskRequest
            {
                Title = title ?? current.Title,
                Notes = notes ?? current.Notes,
                DueAt = newDueAt ?? (clearDueAt ? null : current.DueAt),
                Priority = string.IsNullOrWhiteSpace(priority) ? current.Priority : ToolInput.ParsePriority(priority),
                Tags = tags?.ToList() ?? current.Tags,
            };
            return ToolInput.ToJson(await api.UpdateTaskAsync(id, request, cancellationToken));
        }
    }
}
