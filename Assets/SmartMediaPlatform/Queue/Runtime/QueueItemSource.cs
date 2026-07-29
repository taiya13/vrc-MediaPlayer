namespace SmartMediaPlatform.Queue
{
    /// <summary>
    /// キューに積まれた経緯。自動補充と手動追加を区別するために必要
    /// (自動補充分だけを差し替える、といった将来の制御に使える)。
    /// 将来 DJ Mode / Vote / Shared Queue を足す場合もここに種別を追加する。
    /// </summary>
    public enum QueueItemSource
    {
        /// <summary>利用者が明示的に積んだ。</summary>
        Manual = 0,

        /// <summary>Recommendation Engine の提案として積まれた(自動補充を含む)。</summary>
        Recommendation = 1,
    }
}
