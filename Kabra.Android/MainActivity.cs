using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Xna.Framework;

namespace Kabra.Android;

/// <summary>
/// LAN-only pre-login screen. Never contacts a production endpoint.
/// Assets are extracted asynchronously so users see progress instead of a frozen button.
/// </summary>
[Activity(Label = "Kabra Online 2D LAN", MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Landscape,
    ConfigurationChanges = ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
                           ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : AndroidGameActivity
{
    private const string PrefsFile = "kabra_lan";
    private const string AssetMarker = ".kabra-assets-ready-v2";
    private global::Android.Widget.EditText? _host;
    private global::Android.Widget.EditText? _port;
    private global::Android.Widget.TextView? _message;
    private global::Android.Widget.ProgressBar? _progress;
    private global::Android.Widget.Button? _launch;
    private bool _busy;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ShowLanSetup();
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var detail = args.ExceptionObject?.ToString() ?? "Unknown";
            global::Android.Util.Log.Error("KabraBoot", "Unhandled: " + detail);
        };
    }

    private void ShowLanSetup()
    {
        var prefs = GetSharedPreferences(PrefsFile, FileCreationMode.Private);
        var layout = new global::Android.Widget.LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        layout.SetPadding(32, 20, 32, 20);
        layout.SetBackgroundColor(global::Android.Graphics.Color.Rgb(19, 20, 27));
        var scroll = new global::Android.Widget.ScrollView(this);
        scroll.AddView(layout);

        var heading = new global::Android.Widget.TextView(this)
        {
            Text = "KABRA ONLINE 2D - MODO LAN",
            TextSize = 23
        };
        heading.SetTextColor(global::Android.Graphics.Color.White);
        layout.AddView(heading);
        var help = new global::Android.Widget.TextView(this)
        {
            Text = "PC y celular en el mismo Wi-Fi. Escribi la IPv4 de la PC y el puerto UDP de la COPIA de V48. Ambos campos son obligatorios.",
            TextSize = 15
        };
        help.SetTextColor(global::Android.Graphics.Color.LightGray);
        layout.AddView(help);

        _host = new global::Android.Widget.EditText(this)
        {
            Hint = "IPv4 de la PC: 192.168.1.15",
            Text = prefs?.GetString("host", string.Empty) ?? string.Empty,
            InputType = global::Android.Text.InputTypes.ClassPhone
        };
        layout.AddView(_host);
        _port = new global::Android.Widget.EditText(this)
        {
            Hint = "Puerto UDP de la copia V48 (ejemplo: 5400)",
            Text = prefs?.GetString("port", string.Empty) ?? string.Empty,
            InputType = global::Android.Text.InputTypes.ClassNumber
        };
        layout.AddView(_port);

        _launch = new global::Android.Widget.Button(this) { Text = "GUARDAR Y ENTRAR A KABRA" };
        _launch.Click += async (_, _) => await StartLanClientAsync();
        layout.AddView(_launch);
        _progress = new global::Android.Widget.ProgressBar(this)
        {
            Indeterminate = true,
            Visibility = ViewStates.Gone
        };
        layout.AddView(_progress);
        _message = new global::Android.Widget.TextView(this)
        {
            Text = "Listo para configurar la conexion LAN.",
            TextSize = 15
        };
        _message.SetTextColor(global::Android.Graphics.Color.White);
        layout.AddView(_message);
        var diagnostics = new global::Android.Widget.Button(this) { Text = "VER DIAGNOSTICO DE ARRANQUE" };
        diagnostics.Click += (_, _) =>
        {
            var root = Path.Combine(FilesDir?.AbsolutePath ?? string.Empty, "kabra");
            var log = Path.Combine(root, "kabra_android_boot.log");
            try
            {
                var lines = File.Exists(log)
                    ? File.ReadLines(log).TakeLast(32)
                    : new[] { "No existe el registro. Primero toca GUARDAR Y ENTRAR A KABRA; si ya lo hiciste, esta compilacion no inicio los diagnosticos." };
                ShowStatus(string.Join("\n", lines));
            }
            catch (Exception exception)
            {
                ShowStatus("No se pudo leer el diagnostico: " + exception.Message, true);
            }
        };
        layout.AddView(diagnostics);
        SetContentView(scroll);
    }

    private void ShowStatus(string message, bool isError = false)
    {
        global::Android.Util.Log.Info("KabraLAN", message);
        if (_message == null) return;
        _message.Text = message;
        _message.SetTextColor(isError
            ? global::Android.Graphics.Color.Rgb(255, 140, 140)
            : global::Android.Graphics.Color.White);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (_launch != null) _launch.Enabled = !busy;
        if (_host != null) _host.Enabled = !busy;
        if (_port != null) _port.Enabled = !busy;
        if (_progress != null) _progress.Visibility = busy ? ViewStates.Visible : ViewStates.Gone;
    }

    private async Task StartLanClientAsync()
    {
        if (_busy) return;
        var host = _host?.Text?.Trim() ?? string.Empty;
        var portText = _port?.Text?.Trim() ?? string.Empty;
        if (!IsPrivateIpv4(host))
        {
            ShowStatus("IPv4 no valida. Debe ser privada, por ejemplo 192.168.1.15 (no uses 127.0.0.1).", true);
            return;
        }
        if (!ushort.TryParse(portText, out var port) || port == 0)
        {
            ShowStatus("Falta el puerto UDP de la copia V48. Escribi un numero entre 1 y 65535.", true);
            return;
        }

        SetBusy(true);
        var root = Path.Combine(FilesDir?.AbsolutePath ??
            throw new InvalidOperationException("KABRA_ANDROID_PRIVATE_STORAGE_UNAVAILABLE"), "kabra");
        try
        {
            Directory.CreateDirectory(root);
            Intersect.Client.Core.AndroidBridge.BeginDiagnostics(root);
            Intersect.Client.Core.AndroidBridge.Report("LAN_SETTINGS_VALIDATED");
            // The old client extracted thousands of large files on the UI thread,
            // giving no feedback and appearing completely unresponsive.
            ShowStatus("Preparando recursos de Kabra. La primera vez puede tardar; no cierres la aplicacion...");
            await Task.Run(() =>
            {
                CopyAssetsOnce(root, count =>
                {
                    if (count % 150 == 0)
                        RunOnUiThread(() => ShowStatus($"Preparando archivos del juego: {count} procesados..."));
                });
                ConfigureServer(root, host, port);
                Intersect.Client.Core.AndroidBridge.Report("LAN_RESOURCES_AND_CONFIG_READY");
            });

            GetSharedPreferences(PrefsFile, FileCreationMode.Private)?
                .Edit()?.PutString("host", host)?.PutString("port", port.ToString())?.Apply();
            ShowStatus($"Recursos listos. Iniciando Intersect en {host}:{port}...");
            // Give Android a UI frame to paint the status before starting MonoGame.
            await Task.Delay(100);
            Intersect.Client.Core.AndroidBridge.StageChanged = stage =>
                RunOnUiThread(() => ShowStatus("Arranque Intersect: " + stage));
            await Intersect.Client.Core.AndroidBridge.StartAsync(
                this, root,
                stage => RunOnUiThread(() => ShowStatus("Arranque: " + stage))
            );
            // Android MonoGame Run returns asynchronously: the game view must remain active.
            if (!Intersect.Client.Core.AndroidBridge.ViewAttached)
                throw new InvalidOperationException("KABRA_GAME_VIEW_NOT_ATTACHED; revisa diagnostico de arranque");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("KabraLAN", ex.ToString());
            Intersect.Client.Core.AndroidBridge.Report("LAN_START_FAILED: " + ex);
            ShowStatus("No se pudo iniciar Kabra: " + ex.GetType().Name + ": " + ex.Message, true);
            SetBusy(false);
        }
    }

    private static void ConfigureServer(string root, string host, ushort port)
    {
        var configPath = Path.Combine(root, "resources", "config.json");
        var config = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject()
            ?? throw new InvalidOperationException("KABRA_CONFIG_INVALID");
        config["Host"] = host;
        config["Port"] = (int)port;
        config["UpdateUrl"] = string.Empty;
        File.WriteAllText(configPath, config.ToJsonString());
    }

    private void CopyAssetsOnce(string root, Action<int> progress)
    {
        var marker = Path.Combine(root, AssetMarker);
        var config = Path.Combine(root, "resources", "config.json");
        if (File.Exists(marker) && File.Exists(config))
        {
            progress(150);
            return;
        }
        int count = 0;
        CopyAssets("resources", root, ref count, progress);
        // Written only after completing the entire resource copy.
        File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
    }

    private void CopyAssets(string folder, string destination, ref int count, Action<int> progress)
    {
        var names = Assets?.List(folder)
            ?? throw new InvalidOperationException("KABRA_ASSET_MANAGER_UNAVAILABLE");
        if (names.Length == 0)
            throw new InvalidOperationException("KABRA_ASSETS_MISSING: " + folder);
        foreach (var name in names)
        {
            var child = folder + "/" + name;
            var children = Assets!.List(child);
            if (children is { Length: > 0 })
            {
                CopyAssets(child, destination, ref count, progress);
                continue;
            }
            var targetFile = Path.Combine(destination, child);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            // Atomic write: don't leave a broken file if Android kills the extraction.
            var temporaryFile = targetFile + ".extracting";
            try
            {
                using (var src = Assets.Open(child))
                using (var dst = File.Create(temporaryFile))
                    src.CopyTo(dst);
                File.Move(temporaryFile, targetFile, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
            }
            count++;
            progress(count);
        }
    }

    protected override void OnDestroy()
    {
        Intersect.Client.Core.AndroidBridge.StageChanged = null;
        Intersect.Client.Core.AndroidBridge.Stop();
        base.OnDestroy();
    }

    private static bool IsPrivateIpv4(string text)
    {
        if (!IPAddress.TryParse(text, out var ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork ||
            !string.Equals(ip.ToString(), text, StringComparison.Ordinal))
            return false;
        var b = ip.GetAddressBytes();
        if (b[3] == 0 || b[3] == 255) return false;
        return b[0] == 10 ||
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
            (b[0] == 192 && b[1] == 168);
    }
}