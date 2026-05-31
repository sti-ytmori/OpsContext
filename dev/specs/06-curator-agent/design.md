# 06-curator-agent 設計

Status: 完了

## 上流参照

plan.md の「エージェント設計 / Curator（裏方）」「アーキテクチャ / コンテキスト蓄積ループ」「タイムボックス Day2 7-11h」「Curia流用方針（DecisionLogGeneratorService / FocusUpdateService）」「やらないこと」「検証方法」。
関連: [[04-orchestrator-agents]] [[05-context-store]] [[03-ai-search-tool]]

逸脱メモ: plan.md では CuratorAgent を「Agent Framework エージェント」として定義しているが、Handoff 連鎖には加えず `IHostedService` + `Channel<ConversationEvent>` の fire-and-forget で実装する。Agent Framework の Handoff から外れることで審査員がチャット UI で操作する主要フローを単純に保つ。

## 目的

会話イベント（ConversationEvent）を Channel 経由で受け取り、バックグラウンドで Curia の Detect→Draft→Refine 3段プロンプトを実行して、判断 (decision) または観察 (observation) を ContextEntries に永続化する裏方エージェント。
3〜5 ターンに1回の頻度で起動し、FocusSnapshot を更新することで「案件の現在フォーカス」をロール別に維持する。

## スコープ

やること:
- `IHostedService` 実装（`CuratorHostedService`）と `Channel<ConversationEvent>` による fire-and-forget 受信
- Detect→Draft→Refine 3段プロンプト（Curia `DecisionLogGeneratorService` から移植）
- FocusSnapshot 生成・差分提示ロジック（Curia `FocusUpdateService` から移植）
- `IContextStoreTool.AppendObservationAsync` を通じた永続化
- `ConversationEvent` モデル定義
- Blazor Server 側からの fire-and-forget 発火配線（`IBackgroundQueue<ConversationEvent>` 経由）

やらないこと（plan.md「やらないこと」より）:
- GroupChat / Handoff 連鎖への参加
- ストリーミング応答
- 議事録ペーストフロー（MVP 外）
- Refine のユーザー対話ループ（自動完結のみ。UI で Refine を見せることはしない）

## 設計詳細

### ConversationEvent モデル

```csharp
// OpsContext.Agents/Models/ConversationEvent.cs
public sealed record ConversationEvent(
    string CaseId,
    string Role,
    string Author,
    IReadOnlyList<(string Speaker, string Message)> RecentTurns,
    DateTimeOffset OccurredAt
);
```

`RecentTurns` は直近 5 ターン程度のチャット履歴（Speaker は "user" / "agent"）。Detect に使う。

### IHostedService + Channel 構成

```csharp
// OpsContext.Agents/Services/ICuratorQueue.cs
public interface ICuratorQueue
{
    void Enqueue(ConversationEvent e);  // fire-and-forget
}

// OpsContext.Agents/Services/CuratorHostedService.cs
public sealed class CuratorHostedService(
    ChannelReader<ConversationEvent> reader,
    IChatClient chatClient,
    IContextStoreTool contextStore,
    ILogger<CuratorHostedService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var ev in reader.ReadAllAsync(ct))
            await ProcessAsync(ev, ct);
    }
    // ProcessAsync: Detect → 検出ゼロなら終了、あれば Draft → Refine → Append
}
```

DI 登録:
```csharp
var channel = Channel.CreateBounded<ConversationEvent>(100);
builder.Services.AddSingleton(channel.Writer);
builder.Services.AddSingleton(channel.Reader);
builder.Services.AddSingleton<ICuratorQueue, CuratorQueue>();
builder.Services.AddHostedService<CuratorHostedService>();
```

### Detect→Draft→Refine 3段プロンプト（Curia 移植）

Curia の `DecisionLogGeneratorService` の3メソッド契約をそのまま移植する（移植元: [Curia/Services/DecisionLogGeneratorService.cs](../Curia/Services/DecisionLogGeneratorService.cs)）。

段階 1 — Detect:

システムプロンプト骨子（Curia `BuildDetectSystemPrompt()` ほぼ同一）:
```
直近の会話ターンから暗黙の意思決定・判断・合意を検出してください。
検出ルール: "〜することにした" "〜で進める" "〜は却下" 等の表現を含む発言を対象とする。
非検出: 単なる情報確認・質問・整形のみのターンは検出しない。
出力: JSON配列 [{"summary":"","evidence":"","status":"confirmed|tentative"}]
検出なしの場合は [] のみ出力する。
```

段階 2 — Draft:

検出結果と案件コンテキスト（ReadCaseContextAsync 結果 + 直近ターン）を連結してプロンプトに渡す。
Curia `BuildDraftSystemPrompt()` の Markdown テンプレート構造を流用:
```
# Decision
> Date: {date} / Role: {role} / Status: {status}
## Context
## Chosen
## Why
## Risk
```

段階 3 — Refine:

Draft 結果を `IChatClient` に再度渡し（初回 user/assistant ペア + 指示「簡潔に400字以内に圧縮」）、最終テキストを `IContextStoreTool.AppendObservationAsync` で保存する。
Curia `ChatWithHistoryAsync` 相当: `IChatClient.CompleteAsync(List<ChatMessage>{ system, userFirst, assistantDraft, userRefineInstruction })` を直接呼ぶ。

### FocusSnapshot 生成ロジック（Curia FocusUpdateService 移植）

3〜5 ターンに1回のタイミング（`_turnCounter` でカウント）で FocusSnapshot を生成する。

移植元: [Curia/Services/FocusUpdateService.cs](../Curia/Services/FocusUpdateService.cs)

骨子:
1. `ReadCaseContextAsync(caseId, roleViewpoint=null)` で全エントリ取得
2. `FocusSnapshots` から最新スナップショット取得
3. IChatClient で「全エントリ → ロール別フォーカス要約（Markdown）」を生成
4. `FocusSnapshots` テーブルに INSERT（日付付きで別行として保存。上書きはしない）

システムプロンプト骨子（Curia `FocusUpdateService.BuildSystemPrompt()` 移植）:
```
あなたは案件コンテキストの要約担当です。
以下のエントリ一覧からロール {role} 視点の現在フォーカスを Markdown で出力してください。
出力ルール: 全文のみ出力。コードフェンス禁止。切り詰め禁止。見出し構造を維持する。
```

FocusSnapshot は `IContextStoreTool` の Insert メソッドとして拡張するか、直接 SQL を呼ぶかは実装時に判断（design.md では両案を許容）。

### 起動タイミング制御

- Blazor チャットページで `ICuratorQueue.Enqueue(ev)` を呼ぶのは「ユーザーが判断コメントを入力した」タイミング（「この線で進めます」ボタン押下時）。
- 3〜5 ターンに1回の FocusSnapshot 生成は `CuratorHostedService` 内の `_turnCounter` で管理。

### ファイル配置

```
OpsContext.Agents/
  Models/
    ConversationEvent.cs
  Services/
    ICuratorQueue.cs
    CuratorQueue.cs          -- Channel<ConversationEvent> への Write ラッパー
    CuratorHostedService.cs  -- BackgroundService 本体
    CuratorPrompts.cs        -- Detect / Draft / Refine の static プロンプトビルダー
```

## 依存

- [[04-orchestrator-agents]]: IChatClient が DI に登録済みであること
- [[05-context-store]]: IContextStoreTool.AppendObservationAsync / ReadCaseContextAsync が実装済みであること

## 受け入れ条件

plan.md「検証方法」より:

- ステップ9: 「この線で進めます」ボタン押下後、「Curator: 決定を記録しました」トーストが表示されること（fire-and-forget の完了通知を SignalR か polling で Blazor に届ける）
- ステップ10: `SELECT * FROM ContextEntries WHERE CaseId = '<id>' AND Kind = 'observation'` に Curator が生成した観察テキストが入っていること
- ステップ12: FocusSnapshot が存在し、経理ロールで同案件を開いたとき要約に「A商事 3か月前与信遅延」等が含まれること

## タスク

- [x] `ConversationEvent` モデルと `ICuratorQueue` / `CuratorQueue` を定義し、DI 登録する
- [x] `CuratorHostedService`（BackgroundService）の基本骨格を作り、Channel から読み取れることを確認する
- [x] Detect プロンプトビルダー（`CuratorPrompts.BuildDetectSystemPrompt()`）を実装し、テスト入力で `[{...}]` JSON が返ることを確認する
- [x] Draft プロンプトビルダーを実装し、Detect 結果を渡して Markdown 形式の判断テキストが返ることを確認する
- [x] Refine を実装し（ChatMessage 履歴渡し）、最終テキストを `AppendHandoffAsync` (Kind='handoff') で保存する — observation → handoff に変更（全ロール参照可能）
- [x] Blazor 側で「この線で進めます」ボタン押下時に `ICuratorQueue.Enqueue` を呼ぶ配線を追加する（[[07-blazor-ui]] と協調）
- [x] Curator 完了後のトースト通知: Chat.razor でボタン押下直後に Snackbar 表示（fire-and-forget 発火通知として扱う）
- [x] FocusSnapshot 生成ロジックを実装し、3ターン後に `FocusSnapshots` テーブルに INSERT されることを確認する（Azure 接続後に実 SQL 動作確認）
