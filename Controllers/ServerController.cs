using Microsoft.AspNetCore.Mvc;
using McPanel.Services;

namespace McPanel.Controllers;

public class ServerController : Controller
{
    private readonly MinecraftServerService _serverService;

    public ServerController(MinecraftServerService serverService)
    {
        _serverService = serverService;
    }

    public IActionResult Index()
    {
        ViewBag.Status = _serverService.GetStatus();
        ViewBag.RecentLogs = _serverService.GetRecentLogs();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Start()
    {
        var success = await _serverService.StartServer();
        return Json(new { success, message = success ? "Sunucu başlatıldı!" : "Sunucu zaten çalışıyor!" });
    }

    [HttpPost]
    public async Task<IActionResult> Stop()
    {
        var success = await _serverService.StopServer();
        return Json(new { success, message = success ? "Sunucu durduruldu!" : "Sunucu zaten kapalı!" });
    }

    [HttpPost]
    public async Task<IActionResult> SendCommand([FromBody] CommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
            return Json(new { success = false, message = "Komut boş olamaz!" });

        await _serverService.SendCommand(request.Command);
        return Json(new { success = true });
    }

    [HttpGet]
    public IActionResult Status()
    {
        return Json(_serverService.GetStatus());
    }
}

public class CommandRequest
{
    public string Command { get; set; } = "";
}
