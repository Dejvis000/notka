using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Notka.Api.Data;
using Notka.Api.Exceptions;
using Notka.Api.Hubs;
using Notka.Core;

namespace Notka.Api.Services
{
    internal interface ITaskService
    {
        Task<List<TaskItem>> ListAsync(string? filter, CancellationToken cancellationToken);

        Task<List<TaskItem>> SearchAsync(string query, CancellationToken cancellationToken);

        Task<TaskItem> GetAsync(int id, CancellationToken cancellationToken);

        Task<TaskItem> CreateAsync(TaskRequest request, CancellationToken cancellationToken);

        Task<TaskItem> UpdateAsync(int id, TaskRequest request, CancellationToken cancellationToken);

        Task<TaskItem> CompleteAsync(int id, CancellationToken cancellationToken);

        Task DeleteAsync(int id, CancellationToken cancellationToken);
    }

    internal sealed class TaskService(
        IRepository<TaskItem> tasks,
        TimeProvider time,
        IHubContext<ChangesHub> hub,
        ILogger<TaskService> logger) : ITaskService
    {
        private readonly IRepository<TaskItem> _tasks = tasks;
        private readonly TimeProvider _time = time;
        private readonly IHubContext<ChangesHub> _hub = hub;
        private readonly ILogger<TaskService> _logger = logger;

        public async Task<List<TaskItem>> ListAsync(string? filter, CancellationToken cancellationToken)
        {
            var parsed = ParseFilter(filter);
            var today = _time.GetLocalNow().DateTime.Date;
            var tomorrow = today.AddDays(1);

            var query = _tasks.Query();
            query = parsed switch
            {
                TaskFilter.Today => query.Where(t => !t.IsDone && t.DueAt >= today && t.DueAt < tomorrow),
                TaskFilter.Overdue => query.Where(t => !t.IsDone && t.DueAt < today),
                TaskFilter.Upcoming => query.Where(t => !t.IsDone && t.DueAt >= tomorrow),
                TaskFilter.Done => query.Where(t => t.IsDone),
                _ => query,
            };

            return await Ordered(query).ToListAsync(cancellationToken);
        }

        public async Task<List<TaskItem>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new BusinessRuleException("Zapytanie wyszukiwania nie może być puste.");
            }

            var escaped = query.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            var found = _tasks.Query().Where(t =>
                EF.Functions.Like(t.Title, pattern, "\\") || (t.Notes != null && EF.Functions.Like(t.Notes, pattern, "\\")));

            return await Ordered(found).ToListAsync(cancellationToken);
        }

        public async Task<TaskItem> GetAsync(int id, CancellationToken cancellationToken) =>
            await _tasks.Query().FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new NotFoundException("zadanie", id);

        public async Task<TaskItem> CreateAsync(TaskRequest request, CancellationToken cancellationToken)
        {
            var task = new TaskItem { CreatedAt = _time.GetLocalNow().DateTime };
            Apply(task, request);
            _tasks.Add(task);
            await _tasks.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created task {TaskId}", task.Id);
            await PublishAsync("created", task.Id);
            return task;
        }

        public async Task<TaskItem> UpdateAsync(int id, TaskRequest request, CancellationToken cancellationToken)
        {
            var task = await FindAsync(id, cancellationToken);
            Apply(task, request);
            await _tasks.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated task {TaskId}", id);
            await PublishAsync("updated", id);
            return task;
        }

        public async Task<TaskItem> CompleteAsync(int id, CancellationToken cancellationToken)
        {
            var task = await FindAsync(id, cancellationToken);
            if (!task.IsDone)
            {
                task.IsDone = true;
                task.CompletedAt = _time.GetLocalNow().DateTime;
                await _tasks.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Completed task {TaskId}", id);
                await PublishAsync("updated", id);
            }

            return task;
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken)
        {
            var task = await FindAsync(id, cancellationToken);
            _tasks.Remove(task);
            await _tasks.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted task {TaskId}", id);
            await PublishAsync("deleted", id);
        }

        private async Task<TaskItem> FindAsync(int id, CancellationToken cancellationToken) =>
            await _tasks.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("zadanie", id);

        private static TaskFilter ParseFilter(string? filter)
        {
            if (filter is null)
            {
                return TaskFilter.All;
            }

            if (!Enum.TryParse<TaskFilter>(filter, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                throw new BusinessRuleException("Nieznany filtr. Dozwolone: All, Today, Overdue, Upcoming, Done.");
            }

            return parsed;
        }

        private static IOrderedQueryable<TaskItem> Ordered(IQueryable<TaskItem> query) =>
            query.OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.Priority).ThenBy(t => t.Id);

        private static void Apply(TaskItem task, TaskRequest request)
        {
            var tags = (request.Tags ?? [])
                .Where(t => t is not null)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (tags.Any(t => t.Length > TaskItem.TagMaxLength))
            {
                throw new BusinessRuleException($"Tag może mieć maksymalnie {TaskItem.TagMaxLength} znaków.");
            }

            task.Title = request.Title.Trim();
            task.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes;
            task.DueAt = request.DueAt is { Kind: DateTimeKind.Utc } utc ? utc.ToLocalTime() : request.DueAt;
            task.Priority = request.Priority;
            task.Tags = tags;
        }

        private Task PublishAsync(string kind, int id) =>
            _hub.Clients.All.SendAsync(ChangesHub.TaskChanged, new ChangeEvent { Kind = kind, Id = id }, CancellationToken.None);
    }
}
