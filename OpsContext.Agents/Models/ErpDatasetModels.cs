namespace OpsContext.Agents.Models;

/// <summary>データセット一覧・タブ表示用のメタ情報。</summary>
public sealed record ErpDatasetMeta(
    string Key,
    string DisplayName,
    string Description,
    string Icon);

/// <summary>データセットの表示内容（列名 + 行データ）。</summary>
public sealed record ErpDatasetView(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows);
