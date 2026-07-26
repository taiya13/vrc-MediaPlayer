using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Player;
using SmartMediaPlatform.Recommendation;

namespace SmartMediaPlatform.Session
{
    /// <summary>
    /// セッションの入れ物。作成・取得・削除と「いまどれが有効か」を受け持つ。
    ///
    /// セッションを複数持てるようにしてあるのは、
    /// 将来ワールド内に複数のプレイヤー(部屋ごと・スクリーンごと)を置いても
    /// 上位コードを変えずに済むようにするため。
    /// いまは 1 つだけ作って <see cref="Active"/> を使えばよい。
    /// </summary>
    public sealed class PlayerSessionManager
    {
        private readonly List<PlayerSession> _sessions = new List<PlayerSession>();
        private readonly IReadOnlyList<PlayerSession> _readOnlySessions;

        private int _nextId = 1;

        public PlayerSessionManager()
        {
            _readOnlySessions = _sessions.AsReadOnly();
        }

        public int Count => _sessions.Count;

        public IReadOnlyList<PlayerSession> Sessions => _readOnlySessions;

        /// <summary>いま操作対象になっているセッション。無ければ null。</summary>
        public PlayerSession Active { get; private set; }

        // ───────── 作成 / 登録 ─────────

        /// <summary>
        /// セッションを作って登録する。最初の 1 つは自動で <see cref="Active"/> になる。
        /// ID を省略すると自動で採番する。同じ ID があれば作らずに null を返す。
        /// </summary>
        public PlayerSession Create(
            MediaPlayer player,
            IMediaCatalog catalog,
            RecommendationEngine engine,
            string id = null,
            string name = null,
            Random random = null,
            IBackendLogger logger = null)
        {
            string sessionId = string.IsNullOrWhiteSpace(id) ? GenerateId() : id;
            if (Get(sessionId) != null) return null;

            var session = new PlayerSession(sessionId, player, catalog, engine, random, logger)
            {
                Name = string.IsNullOrEmpty(name) ? sessionId : name,
            };

            _sessions.Add(session);
            if (Active == null) Active = session;
            return session;
        }

        /// <summary>すでに作ってあるセッションを登録する。</summary>
        public bool Register(PlayerSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (Get(session.Id) != null) return false;

            _sessions.Add(session);
            if (Active == null) Active = session;
            return true;
        }

        // ───────── 取得 / 削除 ─────────

        public PlayerSession Get(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return null;

            for (int i = 0; i < _sessions.Count; i++)
            {
                if (string.Equals(_sessions[i].Id, sessionId, StringComparison.OrdinalIgnoreCase))
                    return _sessions[i];
            }
            return null;
        }

        public bool Contains(string sessionId)
        {
            return Get(sessionId) != null;
        }

        /// <summary>
        /// セッションを削除する。有効なセッションを消した場合は、
        /// 残っている先頭のセッションが自動で有効になる。
        /// </summary>
        public bool Delete(string sessionId)
        {
            var session = Get(sessionId);
            if (session == null || !_sessions.Remove(session)) return false;

            if (ReferenceEquals(Active, session))
            {
                Active = _sessions.Count > 0 ? _sessions[0] : null;
            }
            return true;
        }

        public void Clear()
        {
            _sessions.Clear();
            Active = null;
        }

        // ───────── 有効なセッションの切り替え ─────────

        /// <summary>
        /// 操作対象のセッションを切り替える。
        /// 切り替え前のセッションは停止させ、音が重ならないようにする。
        /// </summary>
        public bool SetActive(string sessionId)
        {
            var session = Get(sessionId);
            if (session == null) return false;
            if (ReferenceEquals(Active, session)) return true;

            if (Active != null && Active.IsPlaying) Active.Stop();

            Active = session;
            return true;
        }

        private string GenerateId()
        {
            string id;
            do
            {
                id = $"session-{_nextId:000}";
                _nextId++;
            }
            while (Get(id) != null);

            return id;
        }
    }
}
