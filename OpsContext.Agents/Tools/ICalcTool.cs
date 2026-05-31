namespace OpsContext.Agents.Tools;

// design 08 ICalcTool インターフェース。
// 与信・粗利の決定的計算を担う。LLM に委ねない。
public interface ICalcTool
{
    /// <summary>与信利用可能残高 = limit - used - pendingOrderAmount。負値 = 与信超過。</summary>
    decimal CreditAvailable(decimal limit, decimal used, decimal pendingOrderAmount);

    /// <summary>粗利率 = (price - cost) / price。price &lt;= 0 の場合 0 を返す。</summary>
    decimal GrossMarginRatio(decimal price, decimal cost);
}
