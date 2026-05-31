namespace OpsContext.Agents.Tools;

// design 08 CalcTool 実装。
public sealed class CalcTool : ICalcTool
{
    public decimal CreditAvailable(decimal limit, decimal used, decimal pendingOrderAmount)
        => limit - used - pendingOrderAmount;

    public decimal GrossMarginRatio(decimal price, decimal cost)
        => price <= 0 ? 0m : (price - cost) / price;
}
