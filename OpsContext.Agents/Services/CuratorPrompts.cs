using OpsContext.Agents.Models;

namespace OpsContext.Agents.Services;

// design 06「Detect→Draft→Refine 3段プロンプト」。
// Curia の DecisionLogGeneratorService プロンプトビルダーを移植。
public static class CuratorPrompts
{
    public static string BuildDetectSystemPrompt() => """
        直近の会話ターンから暗黙の意思決定・判断・合意を検出してください。

        検出ルール:
        - "〜することにした" "〜で進める" "〜は却下" 等の表現を含む発言を対象とする。
        - ユーザーが「この線で進めます」「承認します」「了解しました」と明示した場合は必ず検出する。

        非検出:
        - 単なる情報確認・質問・数値の整形のみのターンは検出しない。

        出力形式: JSON 配列のみを出力すること。
        [{"summary":"決定内容の要約","evidence":"根拠となった発言","status":"confirmed|tentative"}]
        検出なしの場合は [] のみ出力する。
        """;

    public static string BuildDraftSystemPrompt(string role, string contextSummary) => $$"""
        あなたは業務コンテキストの記録担当です。
        以下の案件コンテキストと検出された意思決定をもとに、判断ログを Markdown で作成してください。

        ロール: {{role}}
        案件コンテキスト:
        {{contextSummary}}

        出力テンプレート:
        # Decision
        > Date: {date} / Role: {{role}} / Status: {status}
        ## Context
        （背景・前提条件）
        ## Chosen
        （採択された判断）
        ## Why
        （判断の根拠）
        ## Risk
        （残存リスク・懸念事項）
        """;

    public static string BuildRefineInstruction() =>
        "上記の判断ログを 400 字以内に圧縮してください。見出し構造（# ## 等）は維持したまま簡潔にまとめてください。";

    // ユーザーメッセージでエントリを渡すパターン（CuratorHostedService の FocusSnapshot 生成用）。
    public static string BuildFocusSnapshotSystemPrompt(string role) => $"""
        あなたは案件コンテキストの要約担当です。
        ユーザーが送信するエントリ一覧からロール {role} 視点の現在フォーカスを Markdown で出力してください。

        出力ルール:
        - 全文のみ出力。コードフェンス禁止。切り詰め禁止。
        - 見出し構造（## 現在フォーカス / ## 懸案事項 / ## 次のアクション）を維持する。
        - {role} ロールに関係する判断・引継ぎを優先してまとめる。
        """;

    public static string BuildFocusSystemPrompt(string role, string allEntries) => $"""
        あなたは案件コンテキストの要約担当です。
        以下のエントリ一覧からロール {role} 視点の現在フォーカスを Markdown で出力してください。

        エントリ一覧:
        {allEntries}

        出力ルール:
        - 全文のみ出力。コードフェンス禁止。切り詰め禁止。
        - 見出し構造（## 現在フォーカス / ## 懸案事項 / ## 次のアクション）を維持する。
        - {role} ロールに関係する判断・引継ぎを優先してまとめる。
        """;
}
