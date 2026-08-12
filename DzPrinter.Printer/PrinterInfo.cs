namespace DzPrinter.Printer;

/// <summary>
/// 打印机配置信息。对应 JS SDK 中打印作业的配置参数集合。
/// </summary>
public sealed class PrinterInfo
{
    /// <summary>打印机像素宽度（默认 384）。</summary>
    public double PrinterWidth { get; set; } = 384;

    /// <summary>打印机 DPI（默认 203）。</summary>
    public int PrinterDpi { get; set; } = 203;

    /// <summary>标签间隙类型（默认 <see cref="LpaGapType.Unset"/>）。</summary>
    public LpaGapType GapType { get; set; } = LpaGapType.Unset;

    /// <summary>间隙长度（毫米）。</summary>
    public double GapLength { get; set; }

    /// <summary>打印浓度（默认 <see cref="LpaPrintDarkness.Unset"/>）。</summary>
    public LpaPrintDarkness Darkness { get; set; } = LpaPrintDarkness.Unset;

    /// <summary>打印速度（默认 <see cref="LpaPrintSpeed.Unset"/>）。</summary>
    public LpaPrintSpeed Speed { get; set; } = LpaPrintSpeed.Unset;

    /// <summary>打印份数（默认 1）。</summary>
    public int PageCount { get; set; } = 1;
}
