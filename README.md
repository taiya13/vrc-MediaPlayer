# vrc-MediaPlayer

VRChat 向け Smart Media Platform。

## Phase1-1: Catalog System(実装済み)

メディア情報の管理・検索基盤。再生・UI・ネットワークには依存しない独立レイヤー(純粋 C#)。

- コード: `Assets/SmartMediaPlatform/Catalog/Runtime`, `Demo`, `Tests`
- 設計ドキュメント(アーキテクチャ図 / クラス図 / API 一覧 / 設計レビュー / Unity でのテスト方法): [docs/Phase1-1_CatalogSystem.md](docs/Phase1-1_CatalogSystem.md)

## Phase1-2: Udon Compatibility Layer(実装済み)

Phase1-1 の設計思想を維持したまま、UdonSharp で使える Adapter 層に変換。`interface` / `Dictionary` / カスタムクラスを使わず、並列配列 + `int` index 返し + VRChat の `DataDictionary` で Catalog API を再現。

- コード: `Assets/SmartMediaPlatform/Catalog/Runtime/Parallel`(純粋C#・テスト対象)、`Assets/SmartMediaPlatform/Catalog/Udon`(UdonSharp)
- 設計ドキュメント(アーキテクチャ図 / クラス図 / API対応表 / 設計レビュー / Unity・実機でのテスト方法 / 引き継ぎ): [docs/Phase1-2_UdonCompatibilityLayer.md](docs/Phase1-2_UdonCompatibilityLayer.md)

## Phase1-3: Recommendation Engine(実装済み)

Catalog System の上に載る、ルールベース(AI 非使用)のおすすめエンジン。同アーティスト / 同ジャンル / タグ一致 / RelatedIds / ランダム補正の重み付き合計で順位を決定。Catalog API にのみ依存(asmdef 参照レベルで強制)。純粋C#版 + Udon版。

- コード: `Assets/SmartMediaPlatform/Recommendation/Runtime`(純粋C#・テスト対象)、`Assets/SmartMediaPlatform/Recommendation/Udon`(UdonSharp)
- 設計ドキュメント(アーキテクチャ図 / クラス図 / スコアリング仕様 / 期待順位 / 設計レビュー / テスト方法 / 引き継ぎ): [docs/Phase1-3_RecommendationEngine.md](docs/Phase1-3_RecommendationEngine.md)
