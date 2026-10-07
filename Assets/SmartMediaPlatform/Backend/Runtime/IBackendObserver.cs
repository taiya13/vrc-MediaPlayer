namespace SmartMediaPlatform.Backend
{
    /// <summary>
    /// Backend からの通知を受け取る側。
    ///
    /// C# の event ではなく明示的な観測者インターフェースにしてあるのは、
    /// 将来 Udon へ移植する際に delegate / event が使えないため。
    /// Udon では SendCustomEvent による通知に置き換わるが、
    /// 「誰が何を受け取るか」という構造はこのまま流用できる。
    /// </summary>
    public interface IBackendObserver
    {
        void OnBackendEvent(BackendEvent backendEvent);
    }
}
