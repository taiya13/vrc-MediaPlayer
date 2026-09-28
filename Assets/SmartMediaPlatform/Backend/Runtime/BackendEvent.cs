using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Backend
{
    /// <summary>
    /// Backend からの通知 1 件。イミュータブル。
    ///
    /// 状態の前後を両方持たせているので、受け手は
    /// 「何が起きたか」だけでなく「どこからどこへ遷移したか」も判断できる。
    /// </summary>
    public sealed class BackendEvent
    {
        public BackendEventType Type { get; }

        /// <summary>対象メディア。読み込み前のエラーなどでは null になりうる。</summary>
        public MediaItem Item { get; }

        public BackendState PreviousState { get; }

        public BackendState NewState { get; }

        /// <summary>補足メッセージ(ログ用)。</summary>
        public string Message { get; }

        /// <summary>発生元の Backend 名。複数バックエンドを併用したときの識別用。</summary>
        public string BackendName { get; }

        public BackendEvent(
            BackendEventType type,
            MediaItem item,
            BackendState previousState,
            BackendState newState,
            string backendName,
            string message = "")
        {
            Type = type;
            Item = item;
            PreviousState = previousState;
            NewState = newState;
            BackendName = backendName ?? "";
            Message = message ?? "";
        }

        public override string ToString()
        {
            string id = Item != null ? Item.Id : "(none)";
            string suffix = Message.Length > 0 ? $" — {Message}" : "";
            return $"[{BackendName}] {Type}: {id} ({PreviousState} -> {NewState}){suffix}";
        }
    }
}
