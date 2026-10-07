// 重ねるクロスフェード(Phase8-5)を、偽の動画プレイヤーで時間を進めながら通す。
//
// 本物の U# の部品(UdonVideoBackend ×2 / UdonTrackFader ×2 / UdonCrossfadeCoordinator /
// UdonPlayerSession / UdonMediaScreen / UdonMediaController)をそのまま組み合わせ、
// 動画プレイヤーだけを「読み込みに時間がかかる・位置が進む・終わる・失敗する」偽物に替えています。
// 見ているのは、実機で耳に届くもの —— どちらのプレイヤーが、どの音量で鳴っているか —— です。
using System;
using System.Collections.Generic;
using System.Reflection;
using SmartMediaPlatform.Catalog.Udon;
using SmartMediaPlatform.Recommendation.Udon;
using SmartMediaPlatform.World.Udon;
using UnityEngine;
using VRC.SDK3.Components.Video;
using VRC.SDKBase;

/// <summary>遅れて呼ぶ呼び出し(SendCustomEventDelayedSeconds)を、時計が進んだら本当に呼ぶ。</summary>
public static class SimEvents
{
    class Pending
    {
        public object Target;
        public string Name;
        public float At;
    }

    static readonly List<Pending> _pending = new List<Pending>();

    public static void Clear()
    {
        _pending.Clear();
    }

    public static void Schedule(object target, string name, float delay)
    {
        _pending.Add(new Pending { Target = target, Name = name, At = Time.SimNow + delay });
    }

    public static void Invoke(object target, string name)
    {
        MethodInfo m = target.GetType().GetMethod(
            name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, Type.EmptyTypes, null);
        if (m != null) m.Invoke(target, null);
    }

    public static void RunDue()
    {
        for (int guard = 0; guard < 100; guard++)
        {
            Pending due = null;
            foreach (Pending p in _pending)
            {
                if (p.At <= Time.SimNow && (due == null || p.At < due.At)) due = p;
            }
            if (due == null) return;

            _pending.Remove(due);
            Invoke(due.Target, due.Name);
        }
    }
}

/// <summary>1 曲ぶんの性質(偽物の動画プレイヤーが真似る)。</summary>
public class SimMedia
{
    public float Duration = 60f;

    /// <summary>読み込みにかかる秒数。</summary>
    public float LoadSeconds = 3f;

    /// <summary>読み込みに失敗する。</summary>
    public bool Fail;

    /// <summary>「再生」してから、実際に位置が進み始める(音が出る)までの秒数。</summary>
    public float AudioDelay = 0.5f;
}

/// <summary>
/// <b>偽の動画プレイヤー。</b>AVPro と同じ順で知らせを送ります
/// (読み終わった → OnVideoReady、鳴り始めた → OnVideoStart、最後まで → OnVideoEnd、失敗 → OnVideoError)。
/// </summary>
public class SimPlayer : VRC.SDK3.Video.Components.AVPro.VRCAVProVideoPlayer
{
    public UdonVideoBackend Backend;
    public Dictionary<string, SimMedia> Media;

    /// <summary>このワールドの全プレイヤーの読み込み時刻(読み込み間隔の確認用)。</summary>
    public static readonly List<float> LoadTimes = new List<float>();

    string _url;
    SimMedia _media = new SimMedia();
    bool _loading;
    float _loadDoneAt;
    bool _ready;
    bool _playing;
    bool _startSent;
    bool _audioStarted;
    float _time;
    float _playSince;

    public int Loads;

    public override bool IsPlaying { get { return _playing; } }
    public override bool IsReady { get { return _ready; } }

    public override void LoadURL(VRCUrl url)
    {
        _url = url != null ? url.Get() : null;
        _media = _url != null && Media.ContainsKey(_url) ? Media[_url] : new SimMedia();

        _loading = true;
        _loadDoneAt = Time.SimNow + _media.LoadSeconds;
        _ready = false;
        _playing = false;
        _startSent = false;
        _audioStarted = false;
        _time = 0f;

        Loads++;
        LoadTimes.Add(Time.SimNow);
    }

    public override void PlayURL(VRCUrl url)
    {
        LoadURL(url);
    }

    public override void Play()
    {
        if (!_ready || _playing) return;
        _playing = true;
        _playSince = Time.SimNow;
    }

    public override void Pause()
    {
        _playing = false;
    }

    public override void Stop()
    {
        _playing = false;
        _ready = false;
        _loading = false;
        _time = 0f;
        _url = null;
    }

    public override void SetTime(float value)
    {
        _time = value;
    }

    public override float GetTime() { return _time; }

    public override float GetDuration() { return _ready ? _media.Duration : 0f; }

    /// <summary>いま音が出ているか(再生中で、位置が進み始めている)。</summary>
    public bool Audible { get { return _playing && _audioStarted; } }

    public string Url { get { return _url; } }

    public void Advance(float dt)
    {
        if (_loading && Time.SimNow >= _loadDoneAt)
        {
            _loading = false;

            if (_media.Fail)
            {
                Backend.OnVideoError(VideoError.PlayerError);
                return;
            }

            _ready = true;
            Backend.OnVideoReady();
        }

        if (!_playing) return;

        if (!_startSent)
        {
            _startSent = true;
            Backend.OnVideoStart();
            if (!_playing) return;
        }

        if (!_audioStarted && Time.SimNow - _playSince >= _media.AudioDelay) _audioStarted = true;
        if (_audioStarted) _time += dt;

        if (_time >= _media.Duration)
        {
            _time = _media.Duration;
            _playing = false;
            Backend.OnVideoEnd();
        }
    }
}

public static class SimCrossfade
{
    const float Dt = 0.05f;
    const float Audible = 0.01f;

    class XWorld
    {
        public UdonMediaCatalog Catalog;
        public UdonCatalogStore Store;
        public UdonRecommendationEngine Rec;
        public UdonPlayerSession Session;
        public UdonMediaController Controller;
        public UdonMediaScreen Screen;
        public UdonVideoBackend A, B;
        public SimPlayer PA, PB;
        public UdonTrackFader FA, FB;
        public UdonCrossfadeCoordinator X;
        public UdonPlayerOptions Options;
        public Dictionary<string, SimMedia> Media = new Dictionary<string, SimMedia>();

        // 見た結果
        public float Overlap;               // 両方が鳴っていた秒数
        public float MinOverlapPower = 9f;  // 重なっている間の、音量の 2 乗の和のいちばん小さいところ
        public float LongestSilence;        // 再生中なのに、どちらも鳴っていなかった最長の秒数
        public int OverlapEvents;           // 重なりが始まった回数
        float _silence;
        bool _wasOverlapping;
        bool _heardAnything;

        public float VolA { get { return PA.Audible ? Screen.Speaker.volume : 0f; } }
        public float VolB { get { return PB.Audible ? Screen.SpeakerB.volume : 0f; } }

        public void Sample()
        {
            float a = VolA, b = VolB;
            bool overlapping = a > Audible && b > Audible;

            if (overlapping)
            {
                Overlap += Dt;
                float power = a * a + b * b;
                if (power < MinOverlapPower) MinOverlapPower = power;
                if (!_wasOverlapping) OverlapEvents++;
            }
            _wasOverlapping = overlapping;

            bool heard = a > Audible || b > Audible;
            if (heard) _heardAnything = true;

            if (!heard && _heardAnything && Session.IsPlaying) _silence += Dt;
            else _silence = 0f;

            if (_silence > LongestSilence) LongestSilence = _silence;
        }
    }

    static readonly Dictionary<Type, MethodInfo> _updates = new Dictionary<Type, MethodInfo>();

    static void CallPrivate(object target, string name)
    {
        MethodInfo m = target.GetType().GetMethod(
            name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, Type.EmptyTypes, null);
        if (m != null) m.Invoke(target, null);
    }

    static void CallUpdate(object target)
    {
        if (target == null) return;
        Type t = target.GetType();

        MethodInfo m;
        if (!_updates.TryGetValue(t, out m))
        {
            m = t.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            _updates[t] = m;
        }
        if (m != null) m.Invoke(target, null);
    }

    static T Make<T>(string name) where T : Component
    {
        return new GameObject(name).AddComponent<T>();
    }

    static XWorld Build(int songs, float duration, bool crossfade = true, bool withOptions = false,
                        int curve = UdonMediaScreen.CurveEqualPower)
    {
        SimEvents.Clear();
        SimPlayer.LoadTimes.Clear();
        Time.SimNow = 100f;

        var w = new XWorld();

        w.Catalog = Make<UdonMediaCatalog>("Catalog");
        w.Catalog.Ids = new string[songs];
        w.Catalog.Titles = new string[songs];
        w.Catalog.Artists = new string[songs];
        w.Catalog.Genres = new string[songs];
        w.Catalog.Types = new int[songs];
        w.Catalog.Urls = new VRCUrl[songs];
        w.Catalog.Durations = new int[songs];
        w.Catalog.TagValues = new string[0];
        w.Catalog.TagOffsets = new int[songs + 1];
        w.Catalog.RelatedIds = new string[0];
        w.Catalog.RelatedOffsets = new int[songs + 1];

        for (int i = 0; i < songs; i++)
        {
            w.Catalog.Ids[i] = "id" + i;
            w.Catalog.Titles[i] = "曲" + i;
            w.Catalog.Artists[i] = "歌手" + (i % 3);
            w.Catalog.Genres[i] = "J-POP";
            w.Catalog.Types[i] = UdonMediaCatalog.TypeVideo;
            w.Catalog.Urls[i] = new VRCUrl("sim://" + i);
            w.Catalog.Durations[i] = (int)duration;

            w.Media["sim://" + i] = new SimMedia { Duration = duration };
        }

        w.Store = Make<UdonCatalogStore>("Store");
        w.Store.Catalog = w.Catalog;

        w.Rec = Make<UdonRecommendationEngine>("Recommendation");
        w.Rec.Catalog = w.Catalog;

        w.Screen = Make<UdonMediaScreen>("Screen");
        w.Screen.Surface = Make<Renderer>("Surface");
        w.Screen.SurfaceB = Make<Renderer>("SurfaceB");
        w.Screen.Speaker = Make<AudioSource>("Speaker");
        w.Screen.SpeakerB = Make<AudioSource>("SpeakerB");
        w.Screen.Volume = 1f;

        w.Session = Make<UdonPlayerSession>("Session");
        w.Session.Store = w.Store;
        w.Session.Recommendation = w.Rec;
        w.Session.EndBehaviour = UdonPlayerSession.EndBehaviourRecommend;
        w.Session.AutoQueueEnabled = true;
        w.Session.AutoAdvance = true;

        w.A = Make<UdonVideoBackend>("Player");
        w.B = Make<UdonVideoBackend>("PlayerB");
        w.PA = w.A.gameObject.AddComponent<SimPlayer>();
        w.PB = w.B.gameObject.AddComponent<SimPlayer>();
        w.PA.Backend = w.A;
        w.PB.Backend = w.B;
        w.PA.Media = w.Media;
        w.PB.Media = w.Media;

        foreach (UdonVideoBackend backend in new[] { w.A, w.B })
        {
            backend.Catalog = w.Catalog;
            backend.Screen = w.Screen;
            backend.Session = w.Session;
            backend.MinimumLoadInterval = 5f;
        }
        w.A.Player = w.PA;
        w.B.Player = w.PB;

        w.FA = Make<UdonTrackFader>("Fade");
        w.FA.Backend = w.A;
        w.FA.Screen = w.Screen;
        w.FA.Channel = 0;
        w.FA.LogTransitions = false;

        w.FB = Make<UdonTrackFader>("FadeB");
        w.FB.Backend = w.B;
        w.FB.Screen = w.Screen;
        w.FB.Channel = 1;
        w.FB.LogTransitions = false;

        w.X = Make<UdonCrossfadeCoordinator>("Crossfade");
        w.X.Session = w.Session;
        w.X.Screen = w.Screen;
        w.X.BackendA = w.A;
        w.X.BackendB = w.B;
        w.X.FaderA = w.FA;
        w.X.FaderB = w.FB;
        w.X.Enabled = crossfade;
        w.X.LogFade = false;
        w.X.Curve = curve;

        w.Session.Backend = w.A;
        w.Session.Crossfade = w.X;

        if (withOptions)
        {
            w.Options = Make<UdonPlayerOptions>("Options");
            w.Options.Session = w.Session;
            w.Session.Options = w.Options;
        }

        w.Controller = Make<UdonMediaController>("Controller");
        w.Controller.Session = w.Session;

        w.Catalog.EnsureInitialized();
        w.Session.EnsureInitialized();
        CallPrivate(w.Screen, "Start");
        CallPrivate(w.FA, "Start");
        CallPrivate(w.FB, "Start");
        w.X.EnsureInitialized();

        return w;
    }

    static void Step(XWorld w)
    {
        Time.SimNow += Dt;
        SimEvents.RunDue();

        w.PA.Advance(Dt);
        w.PB.Advance(Dt);

        CallUpdate(w.A);
        CallUpdate(w.B);
        CallUpdate(w.FA);
        CallUpdate(w.FB);
        CallUpdate(w.X);
        CallUpdate(w.Options);

        w.Sample();
    }

    static void Run(XWorld w, float seconds)
    {
        int steps = (int)(seconds / Dt + 0.5f);
        for (int i = 0; i < steps; i++) Step(w);
    }

    static bool RunUntil(XWorld w, Func<bool> done, float maxSeconds)
    {
        int steps = (int)(maxSeconds / Dt + 0.5f);
        for (int i = 0; i < steps; i++)
        {
            if (done()) return true;
            Step(w);
        }
        return done();
    }

    /// <summary>曲 0 を流し始め、再生予定に 1, 2, … を積む。</summary>
    static void Start(XWorld w, params int[] queued)
    {
        w.Controller.PlayCatalogIndex(0);
        foreach (int q in queued) w.Session.Enqueue(q);
    }

    static string QueueText(XWorld w)
    {
        string s = "";
        for (int i = 0; i < w.Session.QueueCount; i++) s += (i > 0 ? "," : "") + w.Session.GetQueueAt(i);
        return s;
    }

    static float MinLoadGap()
    {
        float min = 999f;
        for (int i = 1; i < SimPlayer.LoadTimes.Count; i++)
        {
            float gap = SimPlayer.LoadTimes[i] - SimPlayer.LoadTimes[i - 1];
            if (gap < min) min = gap;
        }
        return min;
    }

    static void Check(bool ok, string what) { Sim.Check(ok, what); }
    static void Equal<T>(T expected, T actual, string what) { Sim.Equal(expected, actual, what); }

    // ───────── 流れ ─────────

    // ───────── Prefab ビルダーの確認 ─────────

    static T FindIn<T>(GameObject root, string name) where T : Component
    {
        if (root == null) return null;
        if (root.name == name)
        {
            T here = root.GetComponent<T>();
            if (here != null) return here;
        }

        Transform t = root.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            T found = FindIn<T>(t.GetChild(i).gameObject, name);
            if (found != null) return found;
        }
        return null;
    }

    static object Field(object target, string name)
    {
        if (target == null) return null;
        FieldInfo f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return f != null ? f.GetValue(target) : null;
    }

    static GameObject BuildPrefab(SmartMediaPlatform.Video.VRChat.VideoPlayerPreference preference, out string log)
    {
        Type builder = typeof(SmartMediaPlatform.World.EditorTools.UdonSmartMediaPlayerPrefabBuilder);
        MethodInfo build = builder.GetMethod("BuildCore", BindingFlags.NonPublic | BindingFlags.Static);

        var sb = new System.Text.StringBuilder();
        var root = (GameObject)build.Invoke(null, new object[] { preference, sb });
        log = sb.ToString();
        return root;
    }

    static void CheckPrefab(SmartMediaPlatform.Video.VRChat.VideoPlayerPreference preference)
    {
        string log;
        GameObject root = BuildPrefab(preference, out log);

        Check(root != null, "組み立てられる");
        if (root == null) return;
        Check(!log.Contains("置けませんでした"), "動画プレイヤーを置けている");

        var player = root.GetComponent<UdonSmartMediaPlayer>();
        Check(player != null, "根っこがある");
        if (player == null) return;

        UdonMediaScreen screen = player.Screen;
        Check(screen != null && screen.HasSecondChannel, "画面に 2 系統ぶんの出口がある");
        if (screen == null || !screen.HasSecondChannel) return;

        // ── 前回(Phase7-5)の原因候補 1:音の出口が同じ GameObject に 2 つ付いていた。
        Check(screen.Speaker.gameObject != screen.SpeakerB.gameObject, "2 つの音の出口は別々の GameObject");
        Check(screen.Speaker.gameObject != screen.Surface.gameObject, "音の出口は面とは別の GameObject(面を隠しても音が止まらない)");
        Check(screen.SpeakerB.gameObject != screen.SurfaceB.gameObject, "2 つめの音の出口も面とは別");
        Check(screen.Surface != screen.SurfaceB, "面も 2 枚");
        Check(!screen.SurfaceB.enabled, "2 枚目の面は最初は隠れている");
        Check(screen.MessageRoot != null && screen.MessageText != null, "画面に重ねる文字がつながっている");
        Check(screen.MessageRoot != null && !screen.MessageRoot.activeSelf, "画面の文字は最初は隠れている");
        Check(screen.MessageRoot != null
              && (screen.MessageRoot.GetComponent<Collider>() == null || !screen.MessageRoot.GetComponent<Collider>().enabled),
              "画面の文字には当たり判定が無い(パネルの操作を邪魔しない)");

        UdonVideoBackend a = player.Backend;
        UdonVideoBackend b = player.BackendB;
        Check(a != null && b != null && a != b, "動画プレイヤーが 2 つある");
        if (a == null || b == null) return;

        Check(a.Player != null && b.Player != null && a.Player != b.Player, "それぞれ別の動画プレイヤーを持つ");
        Check(a.gameObject == a.Player.gameObject && b.gameObject == b.Player.gameObject,
              "バックエンドは動画プレイヤーと同じ GameObject(動画の知らせが届く)");
        Check(a.Catalog != null && b.Catalog == a.Catalog, "どちらもカタログを持つ");
        Check(a.Session == player.Session && b.Session == player.Session, "どちらも Session へ知らせる");

        UdonCrossfadeCoordinator x = player.Crossfade;
        Check(x != null, "重ねる担当がある");
        if (x == null) return;

        Check(x.BackendA == a && x.BackendB == b, "重ねる担当に 2 つのプレイヤーがつながっている");
        Check(x.FaderA == player.Fader && x.FaderB == player.FaderB, "重ねる担当に 2 つの音量担当がつながっている");
        Check(x.Screen == screen && x.Session == player.Session, "重ねる担当に画面と Session がつながっている");
        Check(player.Session.Crossfade == x, "Session から重ねる担当が見える");
        Check(player.Sync != null && player.Sync.Crossfade == x, "同期から重ねる担当が見える");

        Check(player.Fader != null && player.Fader.Backend == a && player.Fader.Channel == 0, "音量担当 A は 1 つめのプレイヤーと出口 A");
        Check(player.FaderB != null && player.FaderB.Backend == b && player.FaderB.Channel == 1, "音量担当 B は 2 つめのプレイヤーと出口 B");

        if (preference == SmartMediaPlatform.Video.VRChat.VideoPlayerPreference.AVPro)
        {
            // AVPro は、出口の側に付けた部品が「どのプレイヤーの音か」を持つ。
            var speakerA = screen.Speaker.gameObject.GetComponent<VRC.SDK3.Video.Components.AVPro.VRCAVProVideoSpeaker>();
            var speakerB = screen.SpeakerB.gameObject.GetComponent<VRC.SDK3.Video.Components.AVPro.VRCAVProVideoSpeaker>();
            Check(speakerA != null && speakerA.VideoPlayer == a.Player, "出口 A は 1 つめのプレイヤーの音を出す");
            Check(speakerB != null && speakerB.VideoPlayer == b.Player, "出口 B は 2 つめのプレイヤーの音を出す");

            var surfaceA = screen.Surface.gameObject.GetComponent<VRC.SDK3.Video.Components.AVPro.VRCAVProVideoScreen>();
            var surfaceB = screen.SurfaceB.gameObject.GetComponent<VRC.SDK3.Video.Components.AVPro.VRCAVProVideoScreen>();
            Check(surfaceA != null && surfaceA.VideoPlayer == a.Player, "面 A は 1 つめのプレイヤーを映す");
            Check(surfaceB != null && surfaceB.VideoPlayer == b.Player, "面 B は 2 つめのプレイヤーを映す");
        }
    }

    // ───────── 同期(二人ぶんの手元)─────────

    static UdonSyncCoordinator AttachSync(XWorld w)
    {
        var sync = Make<UdonSyncCoordinator>("Sync");
        sync.Session = w.Session;
        sync.Backend = w.A;
        sync.Controller = w.Controller;
        sync.Crossfade = w.X;
        w.Controller.Sync = sync;
        w.Options.Sync = sync;
        return sync;
    }

    static void AsPlayer(VRCPlayerApi player, Action action)
    {
        Networking.SimLocal = player;
        action();
    }

    /// <summary>持ち主の同期する値([UdonSynced])を、受け取る側へ写して「届いた」を呼ぶ。</summary>
    static void Deliver(UdonSyncCoordinator from, UdonSyncCoordinator to)
    {
        foreach (FieldInfo f in typeof(UdonSyncCoordinator).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (f.GetCustomAttributes(typeof(UdonSharp.UdonSyncedAttribute), false).Length == 0) continue;
            f.SetValue(to, f.GetValue(from));
        }
        to.OnDeserialization();
    }

    /// <summary>二人の手元を同じ時間だけ進める(それぞれ自分のプレイヤーとして)。</summary>
    static bool RunBoth(XWorld a, XWorld b, VRCPlayerApi pa, VRCPlayerApi pb, Func<bool> done, float maxSeconds)
    {
        int steps = (int)(maxSeconds / Dt + 0.5f);
        for (int i = 0; i < steps; i++)
        {
            if (done()) return true;

            Time.SimNow += Dt;
            SimEvents.RunDue();

            foreach (XWorld w in new[] { a, b })
            {
                Networking.SimLocal = w == a ? pa : pb;
                w.PA.Advance(Dt);
                w.PB.Advance(Dt);
                CallUpdate(w.A);
                CallUpdate(w.B);
                CallUpdate(w.FA);
                CallUpdate(w.FB);
                CallUpdate(w.X);
                CallUpdate(w.Options);
                w.Sample();
            }
        }
        return done();
    }

    // ───────── URL(カタログに無い動画)─────────

    /// <summary>URL 欄に URL を貼ったことにする。動画の長さも決めておく。</summary>
    static void PasteUrl(XWorld w, string url, float duration)
    {
        if (w.Options.UrlField == null)
        {
            w.Options.UrlField = new GameObject("UrlField").AddComponent<VRC.SDK3.Components.VRCUrlInputField>();
        }
        w.Media[url] = new SimMedia { Duration = duration };
        w.Options.UrlField.SimText = url;
    }

    /// <summary>いま表のプレイヤーが読んでいる URL。</summary>
    static string FrontUrl(XWorld w)
    {
        UdonVideoBackend front = w.Session.ActiveBackend();
        var player = front != null ? front.Player as SimPlayer : null;
        return player != null ? player.Url : null;
    }

    static bool FrontAudible(XWorld w)
    {
        UdonVideoBackend front = w.Session.ActiveBackend();
        var player = front != null ? front.Player as SimPlayer : null;
        return player != null && player.Audible;
    }

    static void RunUrlScenarios()
    {
        Sim.Scenario("[URL] URL を流している間も「いまの曲」があり、バーの位置が進み、一時停止と再開が効く", () =>
        {
            var w = Build(6, 120f, true, true);
            Start(w);
            Check(RunUntil(w, () => FrontAudible(w), 10f), "曲 0 が鳴り始める");

            PasteUrl(w, "https://example.com/a", 90f);
            w.Options.PlayUrl();

            Check(w.Session.IsExternal, "URL を流している印が立つ");
            Check(w.Session.HasCurrent, "カタログの番号が -1 でも「いまの曲」がある(バーが動く条件)");
            Equal("https://example.com/a", w.Session.ExternalLabel, "見出しに URL が出る");

            Check(RunUntil(w, () => FrontUrl(w) == "https://example.com/a" && FrontAudible(w), 15f),
                  "URL が鳴り始める");
            float t0 = w.Session.ActiveBackend().GetTime();
            Run(w, 2f);
            Check(w.Session.ActiveBackend().GetTime() > t0 + 1.5f, "再生位置が進む(バーが動く)");

            Check(w.Session.TogglePlayPause(), "一時停止を押せる");
            Run(w, 0.5f);
            Check(!w.Session.IsPlaying && !w.Session.ActiveBackend().IsPlaying, "URL が止まる");
            Check(w.Session.IsExternal, "止めても URL のまま(別の曲へ飛ばない)");

            Check(w.Session.TogglePlayPause(), "再開を押せる");
            Run(w, 0.5f);
            Check(w.Session.IsPlaying && FrontUrl(w) == "https://example.com/a" && FrontAudible(w),
                  "同じ URL の続きから鳴る");

            Check(w.Session.Stop(), "停止を押せる");
            Run(w, 0.2f);
            Check(!w.Session.ActiveBackend().IsPlaying, "停止で止まる");
        });

        Sim.Scenario("[URL] 曲を選んだ直後に URL を流しても、読み込みの間隔を空けて、ちゃんと鳴る", () =>
        {
            var w = Build(6, 120f, true, true);
            Start(w);
            Run(w, 0.5f);

            PasteUrl(w, "https://example.com/quick", 90f);
            w.Options.PlayUrl();

            Check(RunUntil(w, () => FrontUrl(w) == "https://example.com/quick" && FrontAudible(w), 15f),
                  "URL が鳴り始める");
            Check(MinLoadGap() >= 4.99f, "読み込みの間隔は 5 秒以上空いている(" + MinLoadGap().ToString("0.0") + ")");
            Check(w.Session.IsExternal, "URL を流している印のまま");
        });

        Sim.Scenario("[URL] 「予定へ」の URL は数えられ、同じ URL は二重に入らず、曲の終わりで先に流れる", () =>
        {
            var w = Build(6, 60f, false, true);
            Start(w, 3);
            Check(RunUntil(w, () => FrontAudible(w), 10f), "曲 0 が鳴り始める");

            PasteUrl(w, "https://example.com/b", 60f);
            w.Options.EnqueueUrl();
            w.Options.EnqueueUrl();
            Equal(1, w.Options.ExternalCount, "同じ URL を 2 回押しても 1 件");
            Equal("https://example.com/b", w.Options.ExternalTextAt(0), "一覧に出す文字は URL");

            Check(RunUntil(w, () => FrontUrl(w) == "https://example.com/b", 70f), "曲 0 のあと URL が流れる");
            Check(w.Session.IsExternal, "URL を流している印が立つ");
            Equal(0, w.Options.ExternalCount, "流した URL は再生予定から外れる");
            Equal("3", QueueText(w), "再生予定の曲はそのまま残る");

            Check(RunUntil(w, () => w.Session.CurrentIndex == 3, 80f), "URL が終わると再生予定の曲 3 へ進む");
            Check(!w.Session.IsExternal, "カタログの曲へ移ったら URL の印は外れる");
        });

        Sim.Scenario("[URL] ▶▶ を押すと、積んだ URL が再生予定の曲より先に流れる / 外せる / 空にできる", () =>
        {
            var w = Build(6, 120f, true, true);
            Start(w, 2);
            Check(RunUntil(w, () => FrontAudible(w), 10f), "曲 0 が鳴り始める");

            PasteUrl(w, "https://example.com/c", 90f);
            w.Options.EnqueueUrl();
            PasteUrl(w, "https://example.com/d", 90f);
            w.Options.EnqueueUrl();
            Equal(2, w.Options.ExternalCount, "2 件積める");

            Check(w.Options.RemoveExternalAt(0), "1 件目を外せる");
            Equal("https://example.com/d", w.Options.ExternalTextAt(0), "残ったのは 2 件目");

            Run(w, 6f);
            Check(w.Session.Next(), "▶▶ を押せる");
            Check(RunUntil(w, () => FrontUrl(w) == "https://example.com/d" && FrontAudible(w), 15f),
                  "URL が先に流れる");
            Equal("2", QueueText(w), "再生予定の曲は残る");

            PasteUrl(w, "https://example.com/e", 90f);
            w.Options.EnqueueUrl();
            w.Session.ClearUpcoming();
            Equal(0, w.Options.ExternalCount, "「予定を空に」で URL も空になる");
            Equal(0, w.Session.QueueCount, "「予定を空に」で曲も空になる");
        });

        Sim.Scenario("[URL] 「1 曲繰り返し」なら、URL の動画も終わったら頭からもう一度流れる", () =>
        {
            var w = Build(6, 60f, true, true);
            w.Options.RepeatMode = UdonPlayerOptions.RepeatOne;
            w.Options.Apply();

            PasteUrl(w, "https://example.com/loop", 50f);
            w.Options.PlayUrl();
            Check(RunUntil(w, () => FrontAudible(w), 15f), "URL が鳴り始める");

            int loads = w.PA.Loads + w.PB.Loads;
            Check(RunUntil(w, () => w.PA.Loads + w.PB.Loads > loads, 60f), "終わったら読み直す");
            Check(RunUntil(w, () => FrontAudible(w), 15f), "また鳴り始める");
            Equal("https://example.com/loop", FrontUrl(w), "同じ URL のまま");
            Check(w.Session.IsExternal && w.Session.CurrentIndex < 0, "おすすめの曲へ進んでいない");

            w.Options.RepeatMode = UdonPlayerOptions.RepeatOff;
            w.Options.Apply();
            Check(RunUntil(w, () => w.Session.CurrentIndex >= 0, 70f), "繰り返しを切れば、終わったあとおすすめへ進む");
        });

        Sim.Scenario("[操作できる人] 切り替えられるのはマスターだけ。切り替えた結果はほかの人にも届く", () =>
        {
            var w = Build(6, 120f, true, true);

            var master = new VRCPlayerApi { playerId = 1, displayName = "マスター", isLocal = true, isMaster = true };
            var guest = new VRCPlayerApi { playerId = 2, displayName = "ゲスト", isLocal = true, isMaster = false };

            var sync = Make<UdonSyncCoordinator>("Sync");
            sync.Session = w.Session;
            sync.Controller = w.Controller;
            w.Options.Sync = sync;
            w.Options.AccessLabel = Make<UnityEngine.UI.Text>("AccessLabel");

            // ゲストの手元。持ち主はゲスト(最後に操作した人)。
            Networking.SimLocal = guest;
            Networking.SimOwner = guest;
            sync.EnsureInitialized();

            w.Options.CycleAccess();
            Equal(UdonSyncCoordinator.AccessEveryone, sync.AccessPolicy, "ゲストが押しても変わらない");

            // マスターの手元。
            Networking.SimLocal = master;
            w.Options.CycleAccess();
            Equal(UdonSyncCoordinator.AccessMasterOnly, sync.AccessPolicy, "マスターが押すと「マスターだけ」になる");
            Check(Networking.SimOwner == master, "配るためにマスターが持ち主になる");
            Equal("操作できる人:マスターだけ", w.Options.AccessLabel.text, "ボタンの文字も変わる");

            // ゲストの手元へ届いたことにする(同期する値を写して、受け取りを呼ぶ)。
            var remote = Make<UdonSyncCoordinator>("SyncRemote");
            remote.Session = Make<UdonPlayerSession>("RemoteSession");
            remote.Session.EnsureInitialized();
            FieldInfo policy = typeof(UdonSyncCoordinator).GetField("_accessPolicy", BindingFlags.NonPublic | BindingFlags.Instance);
            policy.SetValue(remote, policy.GetValue(sync));
            Networking.SimLocal = guest;
            remote.Apply();
            Equal(UdonSyncCoordinator.AccessMasterOnly, remote.AccessPolicy, "ゲストの手元でも「マスターだけ」になる");
            Check(!remote.CanOperate(), "ゲストは操作できない");

            // 「いまの操作者だけ」→「誰でも」と一周する。
            Networking.SimLocal = master;
            w.Options.CycleAccess();
            Equal(UdonSyncCoordinator.AccessOwnerOnly, sync.AccessPolicy, "次は「いまの操作者だけ」");
            w.Options.CycleAccess();
            Equal(UdonSyncCoordinator.AccessEveryone, sync.AccessPolicy, "一周して「誰でも」に戻る");

            Networking.SimLocal = null;
            Networking.SimOwner = null;
        });

        Sim.Scenario("[URL 同期] 持ち主が流した URL は、ほかの人の手元でも同じ URL が流れ、止まらない", () =>
        {
            var owner = new VRCPlayerApi { playerId = 1, displayName = "持ち主", isLocal = true, isMaster = true };
            var guest = new VRCPlayerApi { playerId = 2, displayName = "ゲスト", isLocal = true, isMaster = false };

            var a = Build(6, 120f, true, true);
            var b = Build(6, 120f, true, true);
            UdonSyncCoordinator syncA = AttachSync(a);
            UdonSyncCoordinator syncB = AttachSync(b);

            Networking.SimOwner = owner;
            AsPlayer(owner, () => { syncA.EnsureInitialized(); a.Controller.PlayCatalogIndex(0); });
            AsPlayer(guest, () => { syncB.EnsureInitialized(); Deliver(syncA, syncB); });
            Check(RunBoth(a, b, owner, guest, () => FrontAudible(a) && FrontAudible(b), 15f), "二人とも曲 0 が鳴る");

            // 持ち主が URL を流す。
            AsPlayer(owner, () =>
            {
                PasteUrl(a, "https://example.com/shared", 90f);
                a.Options.PlayUrl();
            });
            AsPlayer(guest, () => Deliver(syncA, syncB));

            Check(b.Session.IsExternal, "ゲストの手元でも URL を流している印が立つ(止まらない)");
            Equal("https://example.com/shared", b.Session.ExternalLabel, "ゲストの見出しも同じ URL");
            Check(RunBoth(a, b, owner, guest,
                          () => FrontUrl(b) == "https://example.com/shared" && FrontAudible(b), 15f),
                  "ゲストの手元でも同じ URL が鳴り始める");
            Check(b.Session.IsPlaying, "ゲストの手元も再生中のまま");

            // 何度届いても、同じ URL は読み直さない。
            int loads = b.PA.Loads + b.PB.Loads;
            AsPlayer(guest, () => { Deliver(syncA, syncB); Deliver(syncA, syncB); });
            Run(b, 0.5f);
            Equal(loads, b.PA.Loads + b.PB.Loads, "同じ URL が何度届いても読み直さない");
            Check(b.Session.IsExternal, "届いたあとも URL の印は立ったまま");

            // 持ち主が URL を「予定へ」入れると、ゲストの再生予定にも出る。
            AsPlayer(owner, () =>
            {
                PasteUrl(a, "https://example.com/next", 90f);
                a.Options.EnqueueUrl();
            });
            AsPlayer(guest, () => Deliver(syncA, syncB));
            Equal(1, b.Options.ExternalCount, "ゲストの再生予定にも URL が 1 件出る");
            Equal("https://example.com/next", b.Options.ExternalTextAt(0), "同じ URL");

            // 持ち主が一時停止すると、ゲストも止まる。
            AsPlayer(owner, () => { a.Session.TogglePlayPause(); syncA.Capture(); });
            AsPlayer(guest, () => Deliver(syncA, syncB));
            Run(b, 0.3f);
            Check(!b.Session.ActiveBackend().IsPlaying, "持ち主が止めると、ゲストの URL も止まる");
            Check(b.Session.IsExternal, "止まっても URL のまま");

            // 持ち主が同じ URL をもう一度流す(1 曲繰り返しと同じ)と、ゲストも頭から読み直す。
            AsPlayer(owner, () =>
            {
                a.Session.TogglePlayPause();
                PasteUrl(a, "https://example.com/shared", 90f);
                a.Options.PlayUrl();
            });
            loads = b.PA.Loads + b.PB.Loads;
            AsPlayer(guest, () => Deliver(syncA, syncB));
            Check(RunBoth(a, b, owner, guest, () => b.PA.Loads + b.PB.Loads > loads, 10f),
                  "同じ URL でも、もう一度流したならゲストも読み直す");

            // 持ち主がカタログの曲へ戻すと、ゲストも戻る。
            AsPlayer(owner, () => a.Controller.PlayCatalogIndex(2));
            AsPlayer(guest, () => Deliver(syncA, syncB));
            Check(!b.Session.IsExternal && b.Session.CurrentIndex == 2, "カタログの曲へ戻ると、ゲストも曲 2 になる");

            Networking.SimLocal = null;
            Networking.SimOwner = null;
        });

        Sim.Scenario("[URL 同期] 「マスターだけ」のとき、マスターでない人は URL を流せない", () =>
        {
            var master = new VRCPlayerApi { playerId = 1, displayName = "マスター", isLocal = true, isMaster = true };
            var guest = new VRCPlayerApi { playerId = 2, displayName = "ゲスト", isLocal = true, isMaster = false };

            var w = Build(6, 120f, true, true);
            UdonSyncCoordinator sync = AttachSync(w);
            sync.AccessPolicy = UdonSyncCoordinator.AccessMasterOnly;
            Networking.SimOwner = master;

            AsPlayer(guest, () =>
            {
                sync.EnsureInitialized();
                PasteUrl(w, "https://example.com/nope", 90f);
                w.Options.PlayUrl();
                w.Options.EnqueueUrl();
            });

            Check(!w.Session.IsExternal, "ゲストが押しても URL は流れない");
            Equal(0, w.Options.ExternalCount, "ゲストが押しても再生予定に入らない");
            Check(Networking.SimOwner == master, "持ち主はマスターのまま");

            Networking.SimLocal = null;
            Networking.SimOwner = null;
        });

        Sim.Scenario("[URL を重ねる] URL の次に積んだ URL は、前の URL と重なって入れ替わる(読み直さない)", () =>
        {
            var w = Build(6, 60f, true, true);

            PasteUrl(w, "https://example.com/first", 60f);
            w.Options.PlayUrl();
            Check(RunUntil(w, () => FrontAudible(w), 15f), "1 本目が鳴り始める");

            PasteUrl(w, "https://example.com/second", 60f);
            w.Options.EnqueueUrl();

            Check(RunUntil(w, () => w.VolA > Audible && w.VolB > Audible, 70f), "2 本目が、1 本目と重なって鳴り始める");
            int loads = w.PA.Loads + w.PB.Loads;

            Check(RunUntil(w, () => w.Session.ExternalLabel == "https://example.com/second", 20f),
                  "1 本目が終わると、2 本目が「いまの曲」になる");
            Equal(loads, w.PA.Loads + w.PB.Loads, "入れ替えるとき読み直さない");
            Equal(0, w.Options.ExternalCount, "2 本目は再生予定から外れる");
            Check(FrontUrl(w) == "https://example.com/second" && FrontAudible(w), "表で 2 本目が鳴り続ける");
            Check(w.Overlap >= 4f, "4 秒以上重なっている(" + w.Overlap.ToString("0.0") + " 秒)");
            Check(w.LongestSilence <= 0.3f, "間に無音ができない(最長 " + w.LongestSilence.ToString("0.00") + " 秒)");
            Check(MinLoadGap() >= 4.99f, "読み込みの間隔は 5 秒以上空いている");
        });

        Sim.Scenario("[URL を重ねる] カタログの曲の次に積んだ URL も重なる / URL の次の曲も重なる", () =>
        {
            var w = Build(6, 60f, true, true);
            Start(w);
            Check(RunUntil(w, () => FrontAudible(w), 10f), "曲 0 が鳴り始める");

            PasteUrl(w, "https://example.com/mid", 60f);
            w.Options.EnqueueUrl();
            w.Session.Enqueue(3);

            Check(RunUntil(w, () => w.VolA > Audible && w.VolB > Audible, 70f), "曲 0 と URL が重なる");
            Check(RunUntil(w, () => w.Session.IsExternal, 20f), "URL が「いまの曲」になる");
            Equal("3", QueueText(w), "曲 3 は再生予定に残る");

            float before = w.Overlap;
            int events = w.OverlapEvents;
            Check(RunUntil(w, () => w.Session.CurrentIndex == 3, 80f), "URL のあと曲 3 へ進む");
            Check(w.OverlapEvents > events && w.Overlap - before >= 4f, "URL と曲 3 も重なる");
            Check(!w.Session.IsExternal, "カタログの曲へ移ったら URL の印は外れる");
            Check(w.LongestSilence <= 0.3f, "間に無音ができない");
        });

        Sim.Scenario("[URL を重ねる] 重なっている最中に ▶▶ を押すと、鳴っている URL をそのまま使う", () =>
        {
            var w = Build(6, 60f, true, true);
            Start(w);
            Check(RunUntil(w, () => FrontAudible(w), 10f), "曲 0 が鳴り始める");

            PasteUrl(w, "https://example.com/skip", 60f);
            w.Options.EnqueueUrl();
            Check(RunUntil(w, () => w.VolA > Audible && w.VolB > Audible, 70f), "重なり始める");

            int loads = w.PA.Loads + w.PB.Loads;
            Check(w.Session.Next(), "▶▶ を押せる");
            Run(w, 0.5f);

            Check(w.Session.IsExternal, "URL が「いまの曲」になる");
            Equal(loads, w.PA.Loads + w.PB.Loads, "読み直さない");
            Check(FrontUrl(w) == "https://example.com/skip" && FrontAudible(w), "URL が鳴り続けている");
            Equal(0, w.Options.ExternalCount, "URL は再生予定から外れる");
        });

        Sim.Scenario("[URL を重ねる] 持ち主でない人の手元でも重なり、同期が届いても読み直さない", () =>
        {
            var owner = new VRCPlayerApi { playerId = 1, displayName = "持ち主", isLocal = true, isMaster = true };
            var guest = new VRCPlayerApi { playerId = 2, displayName = "ゲスト", isLocal = true, isMaster = false };

            var a = Build(6, 60f, true, true);
            var b = Build(6, 60f, true, true);
            UdonSyncCoordinator syncA = AttachSync(a);
            UdonSyncCoordinator syncB = AttachSync(b);
            Networking.SimOwner = owner;

            AsPlayer(owner, () =>
            {
                syncA.EnsureInitialized();
                PasteUrl(a, "https://example.com/one", 60f);
                a.Options.PlayUrl();
                PasteUrl(a, "https://example.com/two", 60f);
                a.Options.EnqueueUrl();
            });
            b.Media["https://example.com/one"] = new SimMedia { Duration = 60f };
            b.Media["https://example.com/two"] = new SimMedia { Duration = 60f };
            AsPlayer(guest, () => { syncB.EnsureInitialized(); Deliver(syncA, syncB); });

            Check(RunBoth(a, b, owner, guest, () => FrontAudible(a) && FrontAudible(b), 15f), "二人とも 1 本目が鳴る");
            Equal(1, b.Options.ExternalCount, "ゲストの再生予定にも 2 本目がある");

            Check(RunBoth(a, b, owner, guest, () => b.VolA > Audible && b.VolB > Audible, 70f),
                  "ゲストの手元でも 2 本目が重なって鳴り始める");
            Check(RunBoth(a, b, owner, guest, () => a.Session.ExternalLabel == "https://example.com/two", 20f),
                  "持ち主は 2 本目へ進む");
            RunBoth(a, b, owner, guest, () => false, 2f);

            int loads = b.PA.Loads + b.PB.Loads;
            AsPlayer(owner, () => syncA.Capture());
            AsPlayer(guest, () => Deliver(syncA, syncB));
            Run(b, 0.5f);

            Equal("https://example.com/two", b.Session.ExternalLabel, "ゲストも 2 本目が「いまの曲」");
            Equal(loads, b.PA.Loads + b.PB.Loads, "同期が届いても読み直さない(頭から鳴り直さない)");
            Check(FrontUrl(b) == "https://example.com/two" && FrontAudible(b), "ゲストの手元で 2 本目が鳴り続ける");

            Networking.SimLocal = null;
            Networking.SimOwner = null;
        });

        Sim.Scenario("[画面の表示] 読み込み中は「読み込み中…」、再生できなければ理由が画面に出る。鳴れば消える", () =>
        {
            var w = Build(6, 120f, true, true);

            w.Screen.MessageRoot = new GameObject("Message");
            w.Screen.MessageRoot.SetActive(false);
            w.Screen.MessageText = Make<UnityEngine.UI.Text>("MessageText");

            var view = Make<SmartMediaPlatform.World.Udon.UI.UdonNowPlayingView>("NowPlaying");
            view.Session = w.Session;
            view.Store = w.Store;
            view.Screen = w.Screen;
            view.StateText = Make<UnityEngine.UI.Text>("State");

            // 壊れた URL。
            w.Options.UrlField = new GameObject("UrlField").AddComponent<VRC.SDK3.Components.VRCUrlInputField>();
            w.Media["https://example.com/broken"] = new SimMedia { Duration = 90f, Fail = true, LoadSeconds = 2f };
            w.Options.UrlField.SimText = "https://example.com/broken";
            w.Options.PlayUrl();

            Run(w, 0.5f);
            view.Refresh();
            Check(w.Screen.MessageRoot.activeSelf, "読み込み中は画面に文字が出る");
            Equal("読み込み中…", w.Screen.MessageText.text, "「読み込み中…」と出る");
            Equal("読み込み中…", view.StateText.text, "帯の状態欄にも出る");

            Run(w, 3f);
            view.Refresh();
            Check(w.Screen.MessageRoot.activeSelf, "再生できなければ画面に文字が出る");
            Check(w.Screen.MessageText.text.StartsWith("再生できませんでした"), "「再生できませんでした」と出る: " + w.Screen.MessageText.text);
            Check(w.Screen.MessageText.text.Contains("エラー 3"), "エラーの番号も出る");
            Equal("再生エラー", view.StateText.text, "帯の状態欄は「再生エラー」");

            // 次はちゃんと流れる URL。
            PasteUrl(w, "https://example.com/ok", 90f);
            w.Options.PlayUrl();
            Check(RunUntil(w, () => FrontAudible(w), 15f), "次の URL は鳴り始める");
            view.Refresh();
            Check(!w.Screen.MessageRoot.activeSelf, "鳴り始めたら画面の文字は消える");
            Equal("", view.StateText.text, "帯の状態欄も空になる");
        });
    }

    public static void Run()
    {
        RunUrlScenarios();

        Sim.Scenario("[おすすめ] カードの「＋」を押しても曲は変わらない(カードの「再生」が一緒に届いても)", () =>
        {
            var w = Build(10, 60f);
            Start(w);
            Run(w, 5f);

            var cards = new GameObject("Cards").AddComponent<SmartMediaPlatform.World.Udon.UI.UdonRecommendationCards>();
            cards.Controller = w.Controller;
            cards.Session = w.Session;
            cards.Store = w.Store;
            cards.Recommendation = w.Rec;
            cards.Cards = new GameObject[6];
            for (int i = 0; i < 6; i++) cards.Cards[i] = new GameObject("Card" + i);
            CallPrivate(cards, "EnsureInitialized");

            FieldInfo shown = cards.GetType().GetField("_shown", BindingFlags.NonPublic | BindingFlags.Instance);
            var ids = (int[])shown.GetValue(cards);
            ids[2] = 7;

            cards.Queue2();
            Time.SimNow += 0.1f;
            cards.Click2();         // 「使う」がカードのほうも拾った

            Equal(0, w.Session.CurrentIndex, "曲は変わらない");
            Equal(0, w.Session.IndexInQueue(7), "再生予定に入る");

            Time.SimNow += 1f;
            cards.Click2();         // しばらくしてからカードを押したら、ちゃんと流れる
            Equal(7, w.Session.CurrentIndex, "カードそのものを押せば流れる");
        });

        Sim.Scenario("[組み立て] AVPro で作った Prefab の配線", () =>
        {
            CheckPrefab(SmartMediaPlatform.Video.VRChat.VideoPlayerPreference.AVPro);
        });

        Sim.Scenario("[組み立て] Unity Video で作った Prefab の配線", () =>
        {
            CheckPrefab(SmartMediaPlatform.Video.VRChat.VideoPlayerPreference.Unity);
        });

        Sim.Scenario("[重ねる] 曲が自然に終わると、次の曲と重なって入れ替わる", () =>
        {
            var w = Build(10, 60f);
            Start(w, 1, 2);

            // 次の曲が聞こえ始めた瞬間の、前の曲と次の曲の音量
            Check(RunUntil(w, () => w.VolB > Audible, 80f), "次の曲が裏で鳴り始める");
            Check(w.VolA > 0.8f, "前の曲はまだ大きい(下がり始めた所で重なる) 前=" + w.VolA);
            Check(w.VolB < 0.2f, "次の曲は小さい所から上がる 次=" + w.VolB);
            Equal(0, w.Session.CurrentIndex, "重なっている間は、まだ前の曲が「いまの曲」");

            Check(RunUntil(w, () => w.Session.CurrentIndex == 1, 20f), "前の曲が終わったら次の曲へ進む");
            Equal(1, w.PB.Loads, "次の曲は読み直していない");
            Equal(1, w.PA.Loads, "前のプレイヤーも読み直していない");
            Check(!w.PA.IsPlaying, "前のプレイヤーは止まる");
            Check(w.PB.Audible, "次の曲は鳴り続けている");
            Check(w.Screen.ShowingB, "画面も次の曲に切り替わっている");
            Equal("2", QueueText(w), "再生予定から次の曲が外れる");

            Check(w.Overlap >= 4f, "4 秒以上重なっている(" + w.Overlap.ToString("0.0") + " 秒)");
            Check(w.MinOverlapPower >= 0.6f, "重なっている間も音が痩せない(2 乗の和の最小 "
                                             + w.MinOverlapPower.ToString("0.00") + ")");
            Check(w.LongestSilence <= 0.3f, "曲の間に無音ができない(最長 " + w.LongestSilence.ToString("0.00") + " 秒)");

            Check(RunUntil(w, () => w.VolB > 0.99f, 10f), "次の曲は全開まで上がる");

            // もう 1 曲。今度は B から A へ。
            Check(RunUntil(w, () => w.Session.CurrentIndex == 2, 80f), "その次の曲へも進む");
            Equal(2, w.A.LoadedIndex, "今度は 1 つめのプレイヤーが次の曲を鳴らしている");
            Equal(2, w.PA.Loads, "1 つめのプレイヤーの読み込みは 2 回だけ");
            Equal(1, w.PB.Loads, "2 つめは 1 回だけ");
            Check(!w.Screen.ShowingB, "画面も 1 つめに戻る");
            Equal(2, w.OverlapEvents, "2 回とも重なった");
            Check(MinLoadGap() >= 4.99f, "読み込みの間隔は 5 秒以上空いている(" + MinLoadGap().ToString("0.0") + ")");
        });

        Sim.Scenario("[重ねる] 裏の読み込みが間に合わなくても、読み直さずにつながる", () =>
        {
            var w = Build(10, 60f);
            w.Media["sim://1"].LoadSeconds = 40f;
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.Session.CurrentIndex == 1, 80f), "前の曲が終わったら次の曲へ進む");
            Check(!w.PB.Audible, "次の曲はまだ読み込み中");
            Equal(0, w.OverlapEvents, "重ならなかった");

            Check(RunUntil(w, () => w.VolB > Audible, 30f), "読み終わったら鳴り始める");
            Check(w.VolB < 0.2f, "鳴り始めは小さい(0 から上がる) " + w.VolB);
            Check(RunUntil(w, () => w.VolB > 0.99f, 10f), "全開まで上がる");

            Equal(1, w.PB.Loads, "読み直していない");
            Equal(1, w.PA.Loads, "前のプレイヤーで読み直していない");
            Check(w.Screen.ShowingB, "画面も次の曲");
        });

        Sim.Scenario("[重ねる] 裏の読み込みに失敗しても、いまの曲は止まらない", () =>
        {
            var w = Build(10, 60f);
            w.Media["sim://1"].Fail = true;
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.PB.Loads == 1 && !w.X.IsBusy, 60f), "裏が失敗して、重ねるのをやめる");
            Equal(0, w.Session.CurrentIndex, "いまの曲はそのまま");
            Check(w.PA.Audible, "いまの曲は鳴り続けている");

            // 前の曲の最後近くまで、途切れずに鳴っている
            Check(RunUntil(w, () => w.PA.GetTime() >= 58f, 30f), "最後近くまで流れる");
            Check(w.PA.Audible, "最後近くでも鳴っている");
            Equal(0, w.Session.CurrentIndex, "まだ前の曲");

            // 前の曲が終わったら、今までの道で次へ(1 は壊れているので飛ばして 2 へ)
            Check(RunUntil(w, () => w.Session.CurrentIndex == 2 && w.PA.Audible, 40f), "壊れた曲を飛ばして、その次が鳴る");
            Check(MinLoadGap() >= 4.99f, "読み込みの間隔は 5 秒以上空いている(" + MinLoadGap().ToString("0.0") + ")");
        });

        Sim.Scenario("[重ねる] 重なっている最中に別の曲を選ぶと、すぐその曲になる", () =>
        {
            var w = Build(10, 60f);
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.VolB > 0.3f, 80f), "重なり始める");

            w.Controller.PlayCatalogIndex(3);

            Check(!w.PB.IsPlaying, "裏で鳴っていた曲は止まる");
            Equal(3, w.Session.CurrentIndex, "選んだ曲になる");
            Check(RunUntil(w, () => w.PA.Audible && w.PA.Url == "sim://3", 15f), "選んだ曲が鳴る");
            Check(!w.PB.Audible, "裏の曲は鳴っていない");
            Check(!w.Screen.ShowingB, "画面は選んだ曲のプレイヤー");
        });

        Sim.Scenario("[重ねる] 重なっている最中に「次へ」を押すと、鳴っている次の曲をそのまま使う", () =>
        {
            var w = Build(10, 60f);
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.VolB > 0.3f, 80f), "重なり始める");

            w.Controller.Next();

            Equal(1, w.Session.CurrentIndex, "次の曲になる");
            Equal(1, w.PB.Loads, "読み直していない");
            Check(!w.PA.IsPlaying, "前の曲は止まる");
            Check(w.PB.Audible, "次の曲は途切れない");
            Check(w.Screen.ShowingB, "画面も次の曲");
            Equal("2", QueueText(w), "再生予定");
        });

        Sim.Scenario("[重ねる] 重なっている最中に一時停止すると、両方止まる。再開すれば続く", () =>
        {
            var w = Build(10, 60f);
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.VolB > 0.2f, 80f), "重なり始める");

            w.Controller.TogglePlayPause();
            Step(w);
            Check(!w.PA.IsPlaying && !w.PB.IsPlaying, "どちらも止まる");
            Equal(0, w.Session.CurrentIndex, "曲は進まない");

            Run(w, 3f);
            Check(!w.PA.IsPlaying && !w.PB.IsPlaying, "止まったまま");

            w.Controller.TogglePlayPause();
            Check(RunUntil(w, () => w.Session.CurrentIndex == 1 && w.PB.Audible, 30f), "再開したら、次の曲まで続く");
            Check(RunUntil(w, () => w.VolB > 0.99f, 15f), "次の曲は全開まで上がる");
        });

        Sim.Scenario("[重ねる] 裏で読み込み中に再生予定を変えると、変えた曲を読み直す", () =>
        {
            var w = Build(10, 60f);
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.X.IsBusy, 60f), "裏で読み込み始める");
            Check(!w.X.IsFading, "まだ重なっていない");

            w.Controller.PlayNextCatalogIndex(5);

            Check(RunUntil(w, () => w.Session.CurrentIndex == 5, 40f), "変えた曲へ進む");
            Equal("sim://5", w.PB.Url, "裏で読んだのは変えた曲");
            Equal("1,2", QueueText(w), "もとの予定は残っている");
            Check(w.OverlapEvents >= 1, "変えた曲とも重なった");
            Check(MinLoadGap() >= 4.99f, "読み込みの間隔は 5 秒以上空いている(" + MinLoadGap().ToString("0.0") + ")");
        });

        Sim.Scenario("[重ねる] 重なっている最中に再生予定を変えても、鳴っている次の曲は切らない", () =>
        {
            var w = Build(10, 60f);
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.VolB > 0.2f, 80f), "重なり始める");

            w.Controller.PlayNextCatalogIndex(5);

            Check(w.PB.Audible, "鳴っている次の曲はそのまま");
            Check(RunUntil(w, () => w.Session.CurrentIndex != 0, 20f), "前の曲が終わる");
            Equal(1, w.Session.CurrentIndex, "重なっていた曲へ進む");
            Equal("5,2", QueueText(w), "入れた曲はそのあと");
        });

        Sim.Scenario("[重ねる] 重なっている最中にバーを戻すと、裏の曲はやめる", () =>
        {
            var w = Build(10, 60f);
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.VolB > 0.2f, 80f), "重なり始める");

            w.Session.ActiveBackend().SetTime(10f);
            Run(w, 0.5f);

            Check(!w.PB.IsPlaying, "裏の曲は止まる");
            Equal(0, w.Session.CurrentIndex, "前の曲のまま");
            Check(RunUntil(w, () => w.VolA > 0.99f, 5f), "前の曲の音量は戻る");

            Check(RunUntil(w, () => w.Session.CurrentIndex == 1 && w.PB.Audible, 80f), "改めて終わりで重なって進む");
        });

        Sim.Scenario("[重ねる] 短い曲では重ねない(重ねない方式でつながる)", () =>
        {
            var w = Build(10, 30f);
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.Session.CurrentIndex == 2 && w.PA.Audible, 120f), "2 曲とも流れる");
            Equal(0, w.PB.Loads, "2 つめのプレイヤーは使わない");
            Equal(0, w.OverlapEvents, "重ならない");
        });

        Sim.Scenario("[重ねる] 「この曲で止める」なら、次の曲は鳴らさない", () =>
        {
            var w = Build(10, 60f, true, true);
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.X.IsBusy, 60f), "裏で読み込み始める");

            w.Options.SleepMinutes = UdonPlayerOptions.SleepAfterTrack;

            Check(RunUntil(w, () => !w.Session.IsPlaying, 40f), "この曲で止まる");
            Equal(0, w.Session.CurrentIndex, "次へ進まない");
            Check(!w.PB.IsPlaying, "裏の曲は鳴っていない");
            Equal(0, w.OverlapEvents, "重ならない");
        });

        Sim.Scenario("[重ねる] おすすめで次を選ぶときも、裏で読んだ曲がそのまま次になる", () =>
        {
            var w = Build(10, 60f);
            Start(w);

            int peek1 = w.Session.PeekNextIndex();
            int peek2 = w.Session.PeekNextIndex();
            Check(peek1 >= 0, "おすすめが選ばれる");
            Equal(peek1, peek2, "覗くたびに選び直さない");

            Check(RunUntil(w, () => w.Session.CurrentIndex != 0, 80f), "次へ進む");
            Equal(peek1, w.Session.CurrentIndex, "裏で読んだ曲が次の曲");
            Equal(1, w.PB.Loads, "読み直していない");
            Check(w.OverlapEvents >= 1, "重なった");
        });

        Sim.Scenario("[重ねる] 何曲も続けて流しても止まらない", () =>
        {
            var w = Build(10, 50f);
            Start(w, 1, 2, 3, 4, 5, 6);

            Check(RunUntil(w, () => w.Session.CurrentIndex == 6 && w.VolA + w.VolB > 0.99f, 400f), "7 曲目まで進む");
            Equal(7, w.PA.Loads + w.PB.Loads, "1 曲につき 1 回だけ読み込む");
            Equal(6, w.OverlapEvents, "6 回とも重なった");
            Check(w.LongestSilence <= 0.3f, "曲の間に無音ができない(最長 " + w.LongestSilence.ToString("0.00") + " 秒)");
            Check(MinLoadGap() >= 4.99f, "読み込みの間隔は 5 秒以上空いている(" + MinLoadGap().ToString("0.0") + ")");
        });

        Sim.Scenario("[重ねる] 重ねない設定なら、今までどおり 1 つのプレイヤーで流す", () =>
        {
            var w = Build(10, 60f, false);
            Start(w, 1, 2);

            Check(RunUntil(w, () => w.Session.CurrentIndex == 2 && w.PA.Audible, 200f), "2 曲とも流れる");
            Equal(0, w.PB.Loads, "2 つめのプレイヤーは使わない");
            Equal(0, w.OverlapEvents, "重ならない");
            Equal(UdonMediaScreen.CurveLinear, w.Screen.FadeCurve, "音量の曲線も今までどおり(まっすぐ)");
        });

        Sim.Scenario("[重ねる] 曲線「なめらか」では、じわっと下がって、じわっと上がる", () =>
        {
            var w = Build(10, 60f, true, false, UdonMediaScreen.CurveSmooth);
            Start(w, 1, 2);
            Equal(UdonMediaScreen.CurveSmooth, w.Screen.FadeCurve, "画面にも曲線が渡っている");

            // 前の曲が下がり始めた所から、2 秒ごとの大きさを見る
            Check(RunUntil(w, () => w.VolA < 0.999f && w.PA.Audible, 80f), "前の曲が下がり始める");
            float a0 = w.VolA; Run(w, 2f);
            float a2 = w.VolA; Run(w, 2f);
            float a4 = w.VolA;
            Check(a2 < a0 && a4 < a2, "下がり続ける " + a0.ToString("0.00") + " → " + a2.ToString("0.00") + " → " + a4.ToString("0.00"));
            Check(a2 < 0.70f, "2 秒で目に見えて下がる(等パワーなら 0.92 のまま) " + a2.ToString("0.00"));
            Check(a4 < 0.35f, "4 秒で半分より小さい(等パワーなら 0.71) " + a4.ToString("0.00"));

            var w2 = Build(10, 60f, true, false, UdonMediaScreen.CurveSmooth);
            Start(w2, 1, 2);
            Check(RunUntil(w2, () => w2.VolB > Audible, 80f), "次の曲が聞こえ始める");
            Run(w2, 2f);
            float b2 = w2.VolB; Run(w2, 2f);
            float b4 = w2.VolB;
            Check(b2 < 0.15f, "上がり始めの 2 秒はまだ小さい(等パワーなら 0.38) " + b2.ToString("0.00"));
            Check(b4 > b2 && b4 < 0.45f, "4 秒でも半分には届かない(じわっと) " + b4.ToString("0.00"));
            Check(RunUntil(w2, () => w2.Session.CurrentIndex == 1, 20f), "入れ替わる");
            Check(RunUntil(w2, () => w2.VolB > 0.99f, 10f), "最後は全開");
            Check(w2.LongestSilence <= 0.3f, "曲の間に無音ができない(最長 " + w2.LongestSilence.ToString("0.00") + " 秒)");
        });

        Sim.Scenario("[重ねる] 再生中に Inspector で秒数や曲線を変えても、その場で効く", () =>
        {
            var w = Build(10, 60f, true, false, UdonMediaScreen.CurveSmooth);
            Start(w, 1, 2);
            Run(w, 5f);

            w.X.Curve = UdonMediaScreen.CurveEqualPower;
            w.X.FadeSeconds = 5f;
            Run(w, 0.3f);

            Equal(UdonMediaScreen.CurveEqualPower, w.Screen.FadeCurve, "曲線が変わる");
            Check(w.FA.FadeOutSeconds == 5f && w.FB.FadeInSeconds == 5f, "両方の音量担当の秒数が変わる");
            Check(RunUntil(w, () => w.Session.CurrentIndex == 1 && w.PB.Audible, 80f), "そのまま次の曲へ重なって進む");
            Check(w.OverlapEvents >= 1, "重なった");
        });

        Sim.Scenario("[重ねる] 持ち主でない人は、手元で入れ替えても曲を進めず、同期を待つ", () =>
        {
            var w = Build(10, 60f);

            // 持ち主から届いた状態(いまの曲 0、再生予定は空、次はおすすめで 4)
            w.Session.AutoAdvance = false;
            w.Session.ApplySyncedState(new int[0], true, 0);
            w.Session.ApplySyncedNext(4);
            Equal(4, w.Session.PeekNextIndex(), "持ち主が選んだ次の曲を使う");
            w.X.FollowRemote(0);

            Check(RunUntil(w, () => w.PB.Audible, 80f), "手元でも次の曲を裏で鳴らし始める");
            Equal("sim://4", w.PB.Url, "持ち主と同じ曲");

            Check(RunUntil(w, () => !w.PA.IsPlaying && w.Screen.ShowingB, 20f), "手元で入れ替わる");
            Equal(0, w.Session.CurrentIndex, "曲は自分では進めない(同期を待つ)");

            // 持ち主から「曲 4 になった」が届く
            w.Session.ApplySyncedState(new int[0], true, 4);
            w.X.FollowRemote(4);
            Run(w, 1f);

            Equal(1, w.PB.Loads, "届いても読み直さない");
            Check(w.PB.Audible, "鳴り続けている");

            // 持ち主でない人は、自分でおすすめを選ばない
            Equal(-1, w.Session.PeekNextIndex(), "次の曲が届くまでは -1");
        });
    }
}
