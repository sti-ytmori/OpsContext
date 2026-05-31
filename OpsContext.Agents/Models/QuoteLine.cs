namespace OpsContext.Agents.Models;

// design 08「QuoteLine モデル」。
// MudBlazor DataGrid での双方向バインド・inline 編集を考慮して class にする。

public enum Verdict { OK, Warning, NG }

public class QuoteLine
{
    // 入力フィールド（Excel 取込時に読み込む列）
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Qty { get; set; }
    public DateOnly RequestedDate { get; set; }
    public decimal UnitPrice { get; set; }

    // 出力フィールド（突合後に書き戻す列）
    public Verdict Verdict { get; set; } = Verdict.OK;
    public string RiskLevel { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string RefNote { get; set; } = "";
}
