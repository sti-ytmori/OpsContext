using OpsContext.Agents.Models;

namespace OpsContext.Agents.Tools;

/// <summary>
/// 基幹スナップショット（ERP 参照データ）の読み取りストア。
/// モックモードはインメモリ、本番モードは Azure SQL から取得する。
/// </summary>
public interface IErpDatasetStore
{
    /// <summary>利用可能なデータセットのメタ情報一覧を返す。</summary>
    IReadOnlyList<ErpDatasetMeta> ListDatasets();

    /// <summary>指定キーのデータセット（列名 + 行データ）を返す。見つからなければ null。</summary>
    ErpDatasetView? GetDataset(string key);

    /// <summary>最終同期日時（UTC）。</summary>
    DateTimeOffset LastSyncedAt { get; }

    /// <summary>
    /// 同期をトリガーする（スタブ）。
    /// 実際には基幹システムとの同期処理は行わず、LastSyncedAt を現在時刻に更新するだけ。
    /// </summary>
    Task<DateTimeOffset> TriggerSyncAsync(CancellationToken ct = default);
}
