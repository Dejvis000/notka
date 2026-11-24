using Microsoft.AspNetCore.SignalR;

namespace Notka.Api.Hubs
{
    public class ChangesHub : Hub
    {
        public const string Path = "/hubs/changes";
        public const string TaskChanged = "TaskChanged";
        public const string NoteChanged = "NoteChanged";
    }
}
