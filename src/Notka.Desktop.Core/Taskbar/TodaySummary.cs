using Notka.Desktop.Services;

namespace Notka.Desktop.Taskbar
{
    public sealed class TodaySummary(INotkaApiClient api, TimeProvider time)
    {
        private readonly INotkaApiClient _api = api;
        private readonly TimeProvider _time = time;

        public async Task<StripContent> LoadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var today = _api.ListTasksAsync("Today", cancellationToken);
                var overdue = _api.ListTasksAsync("Overdue", cancellationToken);
                return StripContent.From(await today, (await overdue).Count, _time.GetLocalNow().DateTime);
            }
            catch (NotkaApiException)
            {
                return StripContent.Unavailable;
            }
        }
    }
}
