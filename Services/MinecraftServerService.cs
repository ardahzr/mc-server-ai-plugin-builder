using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using McPanel.Hubs;

namespace McPanel.Services;

public class MinecraftServerService
{
    private Process? _serverProcess;
    private readonly IHubContext<ConsoleHub> _hubContext;
    private readonly ILogger<MinecraftServerService> _logger;
    private readonly ConcurrentQueue<string> _consoleLog = new();
    private const int MaxLogLines = 500;
    
    public string ServerPath { get; }
    public string JavaPath { get; }
    public string JarFile { get; }
    public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;

    public MinecraftServerService(IHubContext<ConsoleHub> hubContext, IConfiguration config, ILogger<MinecraftServerService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        ServerPath = config["McServer:ServerPath"] ?? "/home/libuntu/Desktop/mc-server";
        JavaPath = config["McServer:JavaPath"] ?? "/usr/lib/jvm/java-21-openjdk/bin/java";
        JarFile = config["McServer:JarFile"] ?? "spigot-1.21.1.jar";
    }

    public async Task<bool> StartServer()
    {
        if (IsRunning) return false;

        // session.lock temizle
        CleanSessionLocks();

        var startInfo = new ProcessStartInfo
        {
            FileName = JavaPath,
            Arguments = $"-Xms2G -Xmx4G -XX:+UseG1GC -jar {JarFile} nogui",
            WorkingDirectory = ServerPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            _serverProcess = Process.Start(startInfo);
            if (_serverProcess == null) return false;

            _ = Task.Run(() => ReadStreamAsync(_serverProcess.StandardOutput));
            _ = Task.Run(() => ReadStreamAsync(_serverProcess.StandardError));

            await BroadcastLog("[Panel] Sunucu başlatılıyor...");
            _logger.LogInformation("Minecraft server started with PID {Pid}", _serverProcess.Id);
            return true;
        }
        catch (Exception ex)
        {
            await BroadcastLog($"[Panel] HATA: {ex.Message}");
            _logger.LogError(ex, "Failed to start server");
            return false;
        }
    }

    public async Task<bool> StopServer()
    {
        if (!IsRunning || _serverProcess == null) return false;

        try
        {
            await SendCommand("stop");
            
            // 10 saniye bekle, kapanmazsa zorla kapat
            if (!_serverProcess.WaitForExit(10000))
            {
                _serverProcess.Kill(true);
                await BroadcastLog("[Panel] Sunucu zorla kapatıldı.");
            }
            else
            {
                await BroadcastLog("[Panel] Sunucu düzgün kapatıldı.");
            }

            _serverProcess = null;
            return true;
        }
        catch (Exception ex)
        {
            await BroadcastLog($"[Panel] Kapatma hatası: {ex.Message}");
            _serverProcess = null;
            return false;
        }
    }

    public async Task SendCommand(string command)
    {
        if (!IsRunning || _serverProcess == null) return;
        
        try
        {
            await _serverProcess.StandardInput.WriteLineAsync(command);
            await _serverProcess.StandardInput.FlushAsync();
            await BroadcastLog($"> {command}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command: {Command}", command);
        }
    }

    public IEnumerable<string> GetRecentLogs() => _consoleLog.ToArray();

    public ServerStatus GetStatus()
    {
        return new ServerStatus
        {
            IsRunning = IsRunning,
            Pid = _serverProcess?.Id,
            ServerPath = ServerPath,
            JarFile = JarFile
        };
    }

    private async Task ReadStreamAsync(StreamReader reader)
    {
        try
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (line != null)
                {
                    await BroadcastLog(line);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading server output");
        }
    }

    private async Task BroadcastLog(string message)
    {
        _consoleLog.Enqueue(message);
        while (_consoleLog.Count > MaxLogLines) _consoleLog.TryDequeue(out _);
        
        await _hubContext.Clients.All.SendAsync("ReceiveLog", message);
    }

    private void CleanSessionLocks()
    {
        try
        {
            foreach (var lockFile in Directory.GetFiles(ServerPath, "session.lock", SearchOption.AllDirectories))
            {
                File.Delete(lockFile);
            }
        }
        catch { }
    }
}

public class ServerStatus
{
    public bool IsRunning { get; set; }
    public int? Pid { get; set; }
    public string ServerPath { get; set; } = "";
    public string JarFile { get; set; } = "";
}
