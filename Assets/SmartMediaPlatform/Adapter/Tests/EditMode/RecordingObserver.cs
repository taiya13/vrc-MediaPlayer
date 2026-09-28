using System.Collections.Generic;
using System.Linq;
using SmartMediaPlatform.Backend;

namespace SmartMediaPlatform.Adapter.Tests
{
    /// <summary>テスト用に通知を記録する観測者。</summary>
    public sealed class RecordingObserver : IBackendObserver
    {
        public List<BackendEvent> Events { get; } = new List<BackendEvent>();

        public void OnBackendEvent(BackendEvent backendEvent)
        {
            Events.Add(backendEvent);
        }

        public BackendEventType[] Types() => Events.Select(e => e.Type).ToArray();

        public void Clear() => Events.Clear();
    }
}
