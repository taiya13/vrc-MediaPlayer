using System.Collections.Generic;
using NUnit.Framework;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Data;
using UnityEngine;

namespace SmartMediaPlatform.Audio.Tests
{
    /// <summary>MediaItem と AudioClip の対応表、および手続き生成の検証。</summary>
    public sealed class AudioClipLibraryTests
    {
        private IMediaCatalog _catalog;
        private AudioClipLibrary _library;
        private readonly List<AudioClip> _createdClips = new List<AudioClip>();

        [SetUp]
        public void SetUp()
        {
            _catalog = new MediaCatalog(new DummyCatalogSource(), new System.Random(1));
            _library = new AudioClipLibrary();
        }

        [TearDown]
        public void TearDown()
        {
            _library.Clear();
            foreach (var clip in _createdClips)
            {
                if (clip != null) Object.DestroyImmediate(clip);
            }
            _createdClips.Clear();
        }

        private AudioClip NewClip(string id = "test")
        {
            var clip = ProceduralClipFactory.Create(id, 0.1f);
            _createdClips.Add(clip);
            return clip;
        }

        [Test]
        public void Register_AssociatesClipWithMediaItem()
        {
            var item = _catalog.FindById("music-001");
            var clip = NewClip("music-001");

            Assert.IsTrue(_library.Register(item, clip));
            Assert.IsTrue(_library.Contains(item));
            Assert.AreSame(clip, _library.Get(item));
            Assert.AreEqual(1, _library.Count);
        }

        [Test]
        public void Lookup_IsCaseInsensitive()
        {
            _library.Register("music-001", NewClip());
            Assert.IsTrue(_library.Contains("MUSIC-001"));
            Assert.IsNotNull(_library.Get("Music-001"));
        }

        [Test]
        public void Register_RejectsEmptyIdOrNullClip()
        {
            Assert.IsFalse(_library.Register("", NewClip()));
            Assert.IsFalse(_library.Register("music-001", null));
            Assert.IsFalse(_library.Register((MediaItem)null, NewClip()));
            Assert.AreEqual(0, _library.Count);
        }

        [Test]
        public void Register_SameIdTwice_Overwrites()
        {
            var first = NewClip("a");
            var second = NewClip("b");

            _library.Register("music-001", first);
            _library.Register("music-001", second);

            Assert.AreEqual(1, _library.Count);
            Assert.AreSame(second, _library.Get("music-001"));
        }

        [Test]
        public void Get_UnknownId_ReturnsNull()
        {
            Assert.IsNull(_library.Get("no-such-id"));
            Assert.IsNull(_library.Get(""));
            Assert.IsFalse(_library.Contains("no-such-id"));
        }

        [Test]
        public void Remove_AndClear_Work()
        {
            _library.Register("music-001", NewClip());
            _library.Register("music-002", NewClip());

            Assert.IsTrue(_library.Remove("music-001"));
            Assert.IsFalse(_library.Remove("music-001"));
            Assert.AreEqual(1, _library.Count);

            _library.Clear();
            Assert.AreEqual(0, _library.Count);
        }

        [Test]
        public void Constructor_AcceptsInspectorEntries()
        {
            var entries = new[]
            {
                new AudioClipEntry("music-001", NewClip("a")),
                new AudioClipEntry("music-002", NewClip("b")),
            };

            var library = new AudioClipLibrary(entries);

            Assert.AreEqual(2, library.Count);
            Assert.IsTrue(library.Contains("music-001"));
        }

        // --- 手続き生成 ---

        [Test]
        public void ProceduralFactory_CreatesPlayableClip()
        {
            var clip = NewClip("music-001");

            Assert.IsNotNull(clip);
            Assert.Greater(clip.samples, 0, "実際に鳴らせるサンプルが入っている");
            Assert.Greater(clip.length, 0f);
        }

        [Test]
        public void ProceduralFactory_IsDeterministicPerId()
        {
            Assert.AreEqual(
                ProceduralClipFactory.FrequencyFor("music-001"),
                ProceduralClipFactory.FrequencyFor("music-001"),
                "同じ ID なら常に同じ音になる");
        }

        [Test]
        public void ProceduralFactory_RejectsInvalidInput()
        {
            Assert.IsNull(ProceduralClipFactory.Create((MediaItem)null));
            Assert.IsNull(ProceduralClipFactory.Create(""));
        }

        [Test]
        public void FillLibrary_CoversEveryMusicItem()
        {
            int created = ProceduralClipFactory.FillLibrary(_library, _catalog, 0.1f);

            var music = _catalog.FilterByType(MediaType.Music);
            Assert.AreEqual(music.Count, created);
            foreach (var item in music) Assert.IsTrue(_library.Contains(item));

            // 後片付けのため生成物を回収する
            foreach (var item in music) _createdClips.Add(_library.Get(item));
        }

        [Test]
        public void FillLibrary_DoesNotOverwriteExistingClips()
        {
            var real = NewClip("real");
            _library.Register("music-001", real);

            ProceduralClipFactory.FillLibrary(_library, _catalog, 0.1f);

            Assert.AreSame(real, _library.Get("music-001"),
                "すでに登録済みの本物の音源は上書きしない");

            foreach (var item in _catalog.FilterByType(MediaType.Music))
            {
                if (item.Id != "music-001") _createdClips.Add(_library.Get(item));
            }
        }
    }
}
