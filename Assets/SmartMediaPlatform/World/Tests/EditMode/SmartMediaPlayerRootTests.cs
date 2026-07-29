using System.Linq;
using NUnit.Framework;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Store;
using SmartMediaPlatform.Library.Playback;
using UnityEngine;

namespace SmartMediaPlatform.World.Tests
{
    /// <summary>
    /// Phase5-1: <b>Prefab をドラッグしただけで動くこと</b>と、
    /// <b>部品を差し替えられること</b>の検証。
    ///
    /// Prefab そのものではなく、Prefab が持つ<b>構造</b>を組み立てて確かめます
    /// (Prefab のファイルが壊れていないかは Unity が読み込む時点で分かるため)。
    /// </summary>
    public sealed class SmartMediaPlayerRootTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        /// <summary>Prefab と同じ形を組み立てる。</summary>
        private SmartMediaPlayerRoot BuildStructure(
            bool withScreen = true, bool withController = true,
            bool withUI = true, bool withCatalog = true, bool withBackend = true)
        {
            _root = new GameObject("SmartMediaPlayer");
            var root = _root.AddComponent<SmartMediaPlayerRoot>();

            if (withScreen)
            {
                var screen = Child("Screen");
                screen.AddComponent<MediaScreen>();

                var surface = Child("Surface", screen.transform);
                surface.AddComponent<MeshRenderer>();
                surface.AddComponent<AudioSource>();
            }

            if (withBackend) Child("Player").AddComponent<DummyMediaBackendProvider>();
            if (withController) Child("Controller").AddComponent<MediaController>();
            if (withUI) Child("UI").AddComponent<MediaPlayerUI>();
            if (withCatalog) Child("Catalog").AddComponent<DefaultCatalogProvider>();

            return root;
        }

        private GameObject Child(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _root.transform, false);
            return go;
        }

        // ───────── ドラッグしただけで動く ─────────

        [Test]
        public void Build_AssemblesEverything()
        {
            var root = BuildStructure();

            Assert.IsTrue(root.Build());

            Assert.IsTrue(root.IsBuilt);
            Assert.IsNotNull(root.Context);
            Assert.IsNotNull(root.Context.Store);
            Assert.IsNotNull(root.Context.Flow);
            Assert.IsNotNull(root.Context.Session);
        }

        [Test]
        public void Build_ShowsTheCatalog()
        {
            var root = BuildStructure();
            root.Build();

            Assert.Greater(root.Flow.Library.Count, 0, "一覧に中身がある");
        }

        [Test]
        public void Build_IsIdempotent()
        {
            var root = BuildStructure();

            Assert.IsTrue(root.Build());
            var first = root.Context;

            Assert.IsTrue(root.Build());
            Assert.AreSame(first, root.Context, "2 回目は組み立て直さない");
        }

        [Test]
        public void PlayFirst_StartsPlayback()
        {
            var root = BuildStructure();
            root.Build();

            Assert.IsTrue(root.PlayFirst());
            Assert.IsNotNull(root.Context.Session.CurrentMediaId);
            Assert.IsNotNull(root.Flow.NowPlaying.Meta);
        }

        // ───────── 部品が無くても動く ─────────

        [Test]
        public void Build_WorksWithoutAnyBackendProvider()
        {
            var root = BuildStructure(withBackend: false);

            Assert.IsTrue(root.Build(), "バックエンドが無くても代役で動く");
            Assert.IsTrue(root.PlayFirst());
        }

        [Test]
        public void Build_WorksWithoutACatalogProvider()
        {
            var root = BuildStructure(withCatalog: false);

            Assert.IsTrue(root.Build());
            Assert.Greater(root.Flow.Library.Count, 0, "既定のカタログが使われる");
        }

        [Test]
        public void Build_WorksWithoutScreenControllerOrUI()
        {
            var root = BuildStructure(withScreen: false, withController: false, withUI: false);

            Assert.IsTrue(root.Build(), "見た目の部品が無くても組み立てられる");
            Assert.IsNull(root.Screen);
            Assert.IsNull(root.Controller);
            Assert.IsNull(root.UI);
        }

        // ───────── 部品が繋がる ─────────

        [Test]
        public void Build_FindsThePartsByInterface()
        {
            var root = BuildStructure();
            root.Build();

            Assert.IsNotNull(root.Screen);
            Assert.IsNotNull(root.Controller);
            Assert.IsNotNull(root.UI);

            Assert.IsInstanceOf<IMediaScreen>(root.Screen);
            Assert.IsInstanceOf<IMediaController>(root.Controller);
            Assert.IsInstanceOf<IMediaPlayerUI>(root.UI);
        }

        [Test]
        public void TheScreenFindsItsSurfaceAndSpeakerOnItsOwn()
        {
            var root = BuildStructure();
            root.Build();

            Assert.IsNotNull(root.Screen.Surface, "Inspector を触らなくても見つかる");
            Assert.IsNotNull(root.Screen.Speaker);
        }

        [Test]
        public void TheControllerIsReadyAfterBuild()
        {
            var root = BuildStructure();
            root.Build();

            var controller = (MediaController)root.Controller;
            Assert.IsTrue(controller.IsReady);
        }

        // ───────── 部品を差し替えられる ─────────

        [Test]
        public void ACustomBackendProviderIsUsedInsteadOfTheDefault()
        {
            _root = new GameObject("SmartMediaPlayer");
            var root = _root.AddComponent<SmartMediaPlayerRoot>();
            var custom = Child("Player").AddComponent<TestBackendProvider>();

            root.Build();

            Assert.AreEqual(1, custom.CreateCount, "差し替えた Provider が呼ばれる");
        }

        [Test]
        public void ACustomCatalogProviderIsUsedInsteadOfTheDefault()
        {
            _root = new GameObject("SmartMediaPlayer");
            var root = _root.AddComponent<SmartMediaPlayerRoot>();
            Child("Player").AddComponent<DummyMediaBackendProvider>();
            var custom = Child("Catalog").AddComponent<TestCatalogProvider>();

            root.Build();

            Assert.AreEqual(1, custom.CreateCount);
            Assert.AreEqual(TestCatalogProvider.ItemCount, root.Flow.Library.Count);
        }

        [Test]
        public void ACustomControllerReplacesTheDefault()
        {
            _root = new GameObject("SmartMediaPlayer");
            var root = _root.AddComponent<SmartMediaPlayerRoot>();
            Child("Player").AddComponent<DummyMediaBackendProvider>();
            var custom = Child("Controller").AddComponent<TestController>();

            root.Build();

            Assert.AreSame(custom, root.Controller);
            Assert.IsTrue(custom.Bound, "差し替えた部品にも組み立て結果が届く");
        }

        [Test]
        public void EveryPartReceivesTheSameContext()
        {
            _root = new GameObject("SmartMediaPlayer");
            var root = _root.AddComponent<SmartMediaPlayerRoot>();
            Child("Player").AddComponent<DummyMediaBackendProvider>();
            var a = Child("A").AddComponent<TestController>();
            var b = Child("B").AddComponent<TestController>();

            root.Build();

            Assert.AreSame(root.Context, a.Context);
            Assert.AreSame(root.Context, b.Context);
        }

        // ───────── ロジックを持っていない ─────────

        [Test]
        public void TheRootDoesNotDecideWhatPlaysNext()
        {
            var type = typeof(SmartMediaPlayerRoot);

            Assert.IsNull(type.GetMethod("Next"));
            Assert.IsNull(type.GetMethod("Play"));
            Assert.IsNull(type.GetMethod("Stop"));
            Assert.IsNull(type.GetMethod("SkipNext"));
        }

        [Test]
        public void TheControllerOnlyRelays()
        {
            // 再生の判断は PlayerSession に残っている
            var root = BuildStructure();
            root.Build();
            root.PlayFirst();

            Assert.IsTrue(root.Context.Session.AutoQueueEnabled,
                "セッションの設定を書き換えていない");
            Assert.Greater(root.Context.Session.Queue.Count, 1,
                "Queue の補充も今までどおり");
        }

        [Test]
        public void TheContextNeverExposesAUrl()
        {
            var root = BuildStructure();
            root.Build();
            root.PlayFirst();

            Assert.IsNull(typeof(MediaPlayerContext).GetProperty("Url"));
            Assert.IsNull(typeof(DisplayMeta).GetProperty("Url"));

            var meta = root.Flow.NowPlaying.Meta;
            Assert.IsNotNull(meta);
            Assert.IsNull(meta.GetType().GetProperty("Url"),
                "Prefab の部品へ渡る型に URL の口が無い");
        }

        [Test]
        public void TheWorldAssemblyDoesNotDependOnTheVRChatSdk()
        {
            var referenced = typeof(SmartMediaPlayerRoot).Assembly
                .GetReferencedAssemblies().Select(x => x.Name).ToArray();

            CollectionAssert.DoesNotContain(referenced, "SmartMediaPlatform.Video.VRChat",
                "SDK が無くても Prefab の骨格は動く");
            CollectionAssert.Contains(referenced, "SmartMediaPlatform.Library.Playback");
        }

        // ───────── 差し替え用のテスト部品 ─────────

        private sealed class TestBackendProvider : MonoBehaviour, IMediaBackendProvider
        {
            public int CreateCount { get; private set; }

            public IMediaBackend CreateBackend(IMediaScreen screen, IBackendLogger logger)
            {
                CreateCount++;
                return new DummyBackend("TestBackend", logger);
            }

            public string Describe() => "テスト用";
        }

        private sealed class TestCatalogProvider : MonoBehaviour, ICatalogProvider
        {
            public const int ItemCount = 2;

            public int CreateCount { get; private set; }

            public IMediaCatalog CreateCatalog()
            {
                CreateCount++;
                return new MediaCatalog(new[]
                {
                    new MediaItem("t-001", "Test One", "Tester", MediaType.Video),
                    new MediaItem("t-002", "Test Two", "Tester", MediaType.Video),
                });
            }

            public string Describe() => "テスト用カタログ";
        }

        private sealed class TestController : MonoBehaviour, IMediaController
        {
            public bool Bound { get; private set; }
            public MediaPlayerContext Context { get; private set; }

            public void Bind(MediaPlayerContext context)
            {
                Bound = true;
                Context = context;
            }

            public bool PlaySelected() => false;
            public bool PlayAt(int index) => false;
            public bool PlayRelatedAt(int index) => false;
            public bool JumpInQueueTo(int index) => false;
            public bool EnqueueSelected() => false;
            public bool PlayNextSelected() => false;
            public bool RemoveFromQueue(int index) => false;
            public bool TogglePlayPause() => false;
            public bool Next() => false;
            public bool Previous() => false;
            public bool Stop() => false;
        }
    }
}
