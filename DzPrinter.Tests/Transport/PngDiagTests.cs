// 临时诊断：PrintPreviewDecoder 问题定位
using DzPrinter.Drawing;
using DzPrinter.Jobs;
using DzPrinter.Printer;
using DzPrinter.Transport.File;
using Xunit.Abstractions;

namespace DzPrinter.Tests.Transport;

public class PngDiagTests
{
    private readonly ITestOutputHelper _out;
    public PngDiagTests(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void Diag_Decode()
    {
        var opts = new DrawJobOptions
        {
            WidthMm = 40, HeightMm = 40, Orientation = 0,
            PrinterInfo = new PrinterInfo { PrinterDpi = 203, PrinterWidth = 384, PageCount = 1 },
        };
        using var ctx = new DrawContext(opts);
        ctx.Start();
        ctx.Canvas.DrawRect(new DrawOptions { X = 5, Y = 5, Width = 20, Height = 20, Fill = true });
        var chunks = ctx.EncodeChunks();

        using var ms = new MemoryStream();
        foreach (var c in chunks) ms.Write(c, 0, c.Length);
        var bytes = ms.ToArray();
        _out.WriteLine($"Total bytes: {bytes.Length}");
        _out.WriteLine($"Hex: {Convert.ToHexString(bytes)}");

        // 解析所有帧：逐帧打印 (offset, cmd, payloadLen, payload hex)
        var i = 0;
        while (i < bytes.Length)
        {
            if (bytes[i] == 27)
            {
                if (i + 2 < bytes.Length && bytes[i + 1] == 74)
                {
                    _out.WriteLine($"  [{i:0000}] ESC J {bytes[i + 2]}");
                    i += 3;
                    continue;
                }
            }
            if (bytes[i] == 0x0C) { _out.WriteLine($"  [{i:0000}] FORM_FEED"); i++; continue; }
            if (bytes[i] != 0x1F) { _out.WriteLine($"  [{i:0000}] Unknown byte {bytes[i]:X2}"); i++; continue; }

            if (i + 2 >= bytes.Length) { _out.WriteLine("  Truncated header"); break; }
            var cmd = bytes[i + 1];
            var len0 = bytes[i + 2];
            int payloadLen;
            int headerLen;
            if (len0 >= 192)
            {
                if (i + 3 >= bytes.Length) { _out.WriteLine("  EBV trunc"); break; }
                payloadLen = ((len0 & 0x3F) << 8) | bytes[i + 3];
                headerLen = 4;
            }
            else
            {
                payloadLen = len0;
                headerLen = 3;
            }
            var ps = i + headerLen;
            var pe = ps + payloadLen;
            var isBitmap = cmd == 41 || cmd == 43 || cmd == 44 || cmd == 45 || cmd == 46 || cmd == 60 || cmd == 61;
            var frameEnd = pe + (isBitmap ? 0 : 1);
            var payloadBytes = bytes.AsSpan(ps, Math.Max(0, Math.Min(payloadLen, bytes.Length - ps))).ToArray();
            var isCrcOk = isBitmap || (frameEnd <= bytes.Length && bytes[frameEnd - 1] == 0x88);
            _out.WriteLine($"  [{i:0000}] 0x1F CMD={cmd}({(PrinterCommand)cmd}) payloadLen={payloadLen} header={headerLen} payloadHex={Convert.ToHexString(payloadBytes)} CRC_ok={isCrcOk} bitmap={isBitmap} frameEnd={frameEnd}");

            i = frameEnd;
        }
    }
}
