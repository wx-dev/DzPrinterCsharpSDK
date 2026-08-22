using DzPrinter.Web.Models;
using DzPrinter.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddSingleton<PrinterService>();
builder.Services.AddControllers();

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// =====================================================================
// 设备管理 API
// =====================================================================

// 发现设备
app.MapGet("/api/devices/discover", async (
    PrinterService svc,
    string? transport,
    CancellationToken ct) =>
{
    try
    {
        var devices = await svc.DiscoverAsync(transport, ct);
        return Results.Ok(new DiscoverResponse
        {
            Devices = devices.Select(d => new DeviceDto
            {
                DeviceId = d.DeviceId,
                Name = d.Name,
                ModelName = d.ModelName,
                Rssi = d.Rssi,
                DeviceType = d.DeviceType.ToString(),
            }).ToList(),
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// 连接设备
app.MapPost("/api/devices/connect", async (
    ConnectRequest req,
    PrinterService svc,
    CancellationToken ct) =>
{
    try
    {
        var result = await svc.ConnectAsync(req.DeviceId, ct);
        var connected = result == DzPrinter.Printer.LpaResult.Ok;
        var dev = svc.ConnectedDevice;
        return Results.Ok(new ConnectResponse
        {
            Success = connected,
            Error = connected ? null : result.ToString(),
            Device = dev != null ? new DeviceDto
            {
                DeviceId = dev.DeviceId,
                Name = dev.DeviceName,
                DeviceType = dev.TransportType.ToString(),
            } : null,
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// 断开设备
app.MapPost("/api/devices/disconnect", async (
    PrinterService svc,
    CancellationToken ct) =>
{
    try
    {
        await svc.DisconnectAsync(ct);
        return Results.Ok(new ApiResult { Success = true });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// 获取连接状态
app.MapGet("/api/devices/status", (PrinterService svc) =>
{
    var dev = svc.ConnectedDevice;
    return Results.Ok(new StatusResponse
    {
        IsConnected = svc.IsConnected,
        Device = dev != null ? new DeviceDto
        {
            DeviceId = dev.DeviceId,
            Name = dev.DeviceName,
            DeviceType = dev.TransportType.ToString(),
        } : null,
        State = svc.IsConnected ? "Connected" : "Disconnected",
    });
});

// 获取打印机硬件信息
app.MapGet("/api/devices/info", async (
    PrinterService svc,
    CancellationToken ct) =>
{
    try
    {
        var hw = await svc.GetPrinterInfoAsync(2000, ct);
        return Results.Ok(new PrinterInfoResponse
        {
            Success = hw != null,
            Hardware = hw != null ? new HardwareInfoDto
            {
                Dpi = hw.Dpi,
                PrinterWidth = hw.PrinterWidth,
                BufferSize = hw.BufferSize,
                BatteryCount = hw.BatteryCount,
                BatteryVoltage = hw.BatteryVoltage,
                ChargeStatus = hw.ChargeStatus,
            } : null,
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new PrinterInfoResponse { Success = false, Error = ex.Message });
    }
});

// 获取可打印状态
app.MapGet("/api/devices/printable-status", async (
    PrinterService svc,
    CancellationToken ct) =>
{
    try
    {
        var status = await svc.GetPrintableStatusAsync(2000, ct);
        return Results.Ok(new PrintableStatusResponse
        {
            Success = status != null,
            StatusCode = status?.ToString(),
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new PrintableStatusResponse { Success = false, Error = ex.Message });
    }
});

// =====================================================================
// 打印 API
// =====================================================================

// 预览（只绘制不打印，返回 Base64）
app.MapPost("/api/print/preview", (PrintRequest req, PrinterService svc) =>
{
    try
    {
        var previewBase64 = svc.Preview(req);
        return Results.Ok(new { success = previewBase64 != null, previewBase64 });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// 按绘制指令打印
app.MapPost("/api/print", async (
    PrintRequest req,
    PrinterService svc,
    CancellationToken ct) =>
{
    try
    {
        var (result, chunks, previewPath, previewBase64) = await svc.PrintAsync(req, ct);
        return Results.Ok(new PrintResponse
        {
            Success = result == DzPrinter.Printer.LpaResult.Ok,
            Error = result != DzPrinter.Printer.LpaResult.Ok ? result.ToString() : null,
            ChunksSent = chunks,
            PreviewPath = previewPath,
            PreviewBase64 = previewBase64,
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// 发送原始数据
app.MapPost("/api/print/raw", async (
    RawPrintRequest req,
    PrinterService svc,
    CancellationToken ct) =>
{
    try
    {
        var bytes = Convert.FromBase64String(req.Base64Data);
        var result = await svc.SendRawAsync(bytes, ct);
        return Results.Ok(new ApiResult
        {
            Success = result == DzPrinter.Printer.LpaResult.Ok,
            Error = result != DzPrinter.Printer.LpaResult.Ok ? result.ToString() : null,
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

app.Run();
