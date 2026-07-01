using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Reflection;
using VRCFaceTracking;
using VRCFTPicoModule.Utils;
using static VRCFTPicoModule.Utils.Localization;

namespace VRCFTPicoModule;

public class VRCFTPicoModule : ExtTrackingModule
{
    private static readonly int[] Ports = [29765, 29763];
    private static readonly UdpClient[] Clients = Ports.Select(port => new UdpClient(port) { Client = { ReceiveTimeout = 100 } }).ToArray();
    private static UdpClient _udpClient = new();
    private static int _port;
    private Updater? _updater;
    private Config? _config;
    private RawValueLogger? _rawLogger;
    private (bool, bool) _trackingAvailable;

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        Localization.Initialize(CultureInfo.CurrentUICulture.Name);
        Logger.LogInformation(T("start-init"));

        _config = LoadConfig();
        _trackingAvailable = (
            !_config.DisableEye && eyeAvailable,
            !_config.DisableExpression && expressionAvailable
        );

        var initializationResult = InitializeAsync().GetAwaiter().GetResult();
        UpdateModuleInfo(initializationResult);

        return initializationResult;
    }

    private async Task<(bool eyeSuccess, bool expressionSuccess)> InitializeAsync()
    {
        Logger.LogDebug(T("initializing-udp-clients"), string.Join(", ", Ports));

        var portIndex = await ListenOnPorts();
        if (portIndex == -1) return (false, false);

        _port = Ports[portIndex];
        _udpClient = new UdpClient(_port);
        Logger.LogInformation(T("using-port"), _port);

        if (!_trackingAvailable.Item1)
            Logger.LogInformation(T("eye-tracking-disabled"));
        if (!_trackingAvailable.Item2)
            Logger.LogInformation(T("expression-tracking-disabled"));

        if (_config!.LogRaw)
        {
            _rawLogger = new RawValueLogger(
                _config.ResolveLogPath(),
                _config.LogIntervalMs,
                _config.LogIncludeVisemes,
                Logger);
            _rawLogger.Start();
        }

        _updater = new Updater(_udpClient, Logger, _port == Ports[1], _trackingAvailable, _config, _rawLogger);

        return _trackingAvailable;
    }

    private Config LoadConfig()
    {
        var currentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        Logger.LogInformation(T("config-path"), currentDirectory);

        if (string.IsNullOrEmpty(currentDirectory) || !Directory.Exists(currentDirectory))
            return new Config();

        var config = Config.Load(currentDirectory, Logger);

        if (config.DisableEye)
            Logger.LogInformation(T("force-disable-eye"));
        if (config.DisableExpression)
            Logger.LogInformation(T("force-disable-expression"));

        return config;
    }

    private void UpdateModuleInfo((bool, bool) initializationResult)
    {
        var moduleProtocol = _port == Ports[1] ? $" [{T("legacy-protocol")}]" : "";
        var moduleTrackingStatus = initializationResult switch
        {
            { Item1: true, Item2: true } => T("full-face-tracking"),
            { Item1: true, Item2: false } => T("eye-tracking"),
            { Item1: false, Item2: true } => T("expression-tracking"),
            _ => ""
        };
        ModuleInformation.Name = "PICO / " + moduleTrackingStatus + moduleProtocol;
        var stream = GetType().Assembly.GetManifestResourceStream("VRCFTPicoModule.Assets.pico.png");
        ModuleInformation.StaticImages = stream != null ? [stream] : ModuleInformation.StaticImages;
    }

    private async Task<int> ListenOnPorts()
    {
        try
        {
            var tasks = Clients.Select(client => client.ReceiveAsync()).ToArray();
        
            if (tasks.Length == 0)
            {
                return -1;
            }
        
            var completedTask = await Task.WhenAny(tasks);

            foreach (var client in Clients) client.Dispose();
        
            return Array.IndexOf(tasks, completedTask);
        }
        catch (Exception ex)
        {
            Logger.LogError(T("init-failed"), ex);
        }
    
        return -1;
    }

    public override void Update()
    {
        _updater?.Update(Status);
    }

    public override void Teardown()
    {
        foreach (var client in Clients)
        {
            client.Dispose();
        }
        _udpClient.Dispose();
        _updater = null;
        _rawLogger?.Dispose();
        _rawLogger = null;
    }
}