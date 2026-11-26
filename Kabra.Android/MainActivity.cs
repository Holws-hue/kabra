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
[Activity(Label = "Kabra 2D LAN FIX", MainLauncher = true,
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
    private global::Android.Widget.CheckBox? _safeGraphics;
    private bool _busy;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var root = Path.Combine(FilesDir?.AbsolutePath ??
            throw new InvalidOperationException("ANDROID_APP_PRIVATE_DIRECTORY_MISSING"), "kabra");
        Intersect.Client.Core.AndroidBridge.ConfigureDiagnosticStorage(root);
        // MonoGame's default Android mode blocks rendering through the UI looper;
        // allow a selectable separate render thread for Mali GPUs and slow devices.
        // SurfaceView on the Activity looper is the conservative default for
        // Mali/Adreno GLES. The worker mode stays available as an alternate.
        RenderOnUIThread = true;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Intersect.Client.Core.AndroidBridge.ReportFatal("APPDOMAIN_UNHANDLED", ex);
            else
                Intersect.Client.Core.AndroidBridge.Report("APPDOMAIN_UNHANDLED_NONEXCEPTION");
        };
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
            Intersect.Client.Core.AndroidBridge.ReportFatal("ANDROID_UNHANDLED", args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            Intersect.Client.Core.AndroidBridge.ReportFatal("TASK_UNOBSERVED", args.Exception);

        ShowLanSetup();
    }

    private int Dp(float amount) =>
        (int)(amount * (Resources?.DisplayMetrics?.Density ?? 1f));

    private global::Android.Graphics.Drawables.GradientDrawable PanelBackground(bool accent = false)
    {
        var d = new global::Android.Graphics.Drawables.GradientDrawable();
        d.SetColor(accent
            ? global::Android.Graphics.Color.Rgb(135, 24, 33)
            : global::Android.Graphics.Color.Rgb(29, 29, 38));
        d.SetCornerRadius(Dp(12));
        d.SetStroke(Dp(1), global::Android.Graphics.Color.Rgb(147, 102, 50));
        return d;
    }

    private global::Android.Widget.Button StyledButton(string label, bool primary = false)
    {
        var button = new global::Android.Widget.Button(this)
        {
            Text = label,
            TextSize = 13
        };
        button.SetTextColor(global::Android.Graphics.Color.White);
        button.Background = PanelBackground(primary);
        button.SetPadding(Dp(12), Dp(5), Dp(12), Dp(5));
        return button;
    }

    private void ShowLanSetup()
    {
        var prefs = GetSharedPreferences(PrefsFile, FileCreationMode.Private);
        var scroll = new global::Android.Widget.ScrollView(this);
        scroll.SetBackgroundColor(global::Android.Graphics.Color.Rgb(12, 12, 19));
        var layout = new global::Android.Widget.LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        layout.SetPadding(Dp(22), Dp(9), Dp(22), Dp(10));
        scroll.AddView(layout);

        var titleRow = new global::Android.Widget.LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        titleRow.SetGravity(global::Android.Views.GravityFlags.CenterVertical);
        var iconId = Resources?.GetIdentifier("kabra_launcher", "drawable", PackageName) ?? 0;
        if (iconId != 0)
        {
            var logo = new global::Android.Widget.ImageView(this);
            logo.SetImageResource(iconId);
            titleRow.AddView(logo,
                new global::Android.Widget.LinearLayout.LayoutParams(Dp(62), Dp(62)));
        }
        else
        {
            var badge = new global::Android.Widget.TextView(this)
            {
                Text = "K",
                TextSize = 32,
                Gravity = global::Android.Views.GravityFlags.Center
            };
            badge.SetTextColor(global::Android.Graphics.Color.Rgb(255, 211, 129));
            badge.Background = PanelBackground(true);
            titleRow.AddView(badge,
                new global::Android.Widget.LinearLayout.LayoutParams(Dp(58), Dp(58)));
        }

        var headings = new global::Android.Widget.LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        headings.SetPadding(Dp(14), 0, 0, 0);
        var title = new global::Android.Widget.TextView(this)
        {
            Text = "KABRA ONLINE 2D",
            TextSize = 23
        };
        title.SetTextColor(global::Android.Graphics.Color.Rgb(250, 209, 129));
        headings.AddView(title);
        var subtitle = new global::Android.Widget.TextView(this)
        {
            Text = "MODO LAN  |  V48  |  ANDROID ARM64",
            TextSize = 12
        };
        subtitle.SetTextColor(global::Android.Graphics.Color.LightGray);
        headings.AddView(subtitle);
        titleRow.AddView(headings);
        layout.AddView(titleRow);

        var help = new global::Android.Widget.TextView(this)
        {
            Text = "PC y celular en el mismo Wi-Fi. Usa la IPv4 de tu PC y el puerto UDP de la copia V48.",
            TextSize = 13
        };
        help.SetTextColor(global::Android.Graphics.Color.LightGray);
        help.SetPadding(0, Dp(5), 0, Dp(5));
        layout.AddView(help);

        var fields = new global::Android.Widget.LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        _host = new global::Android.Widget.EditText(this)
        {
            Hint = "IP de la PC: 192.168.1.15",
            Text = prefs?.GetString("host", string.Empty) ?? string.Empty,
            InputType = global::Android.Text.InputTypes.ClassPhone,
            TextSize = 17
        };
        _host.SetSingleLine(true);
        _host.SetTextColor(global::Android.Graphics.Color.White);
        _host.SetHintTextColor(global::Android.Graphics.Color.LightGray);
        fields.AddView(_host, new global::Android.Widget.LinearLayout.LayoutParams(
            0, Dp(58), 3f));
        _port = new global::Android.Widget.EditText(this)
        {
            Hint = "Puerto UDP",
            Text = prefs?.GetString("port", string.Empty) ?? string.Empty,
            InputType = global::Android.Text.InputTypes.ClassNumber,
            TextSize = 17
        };
        _port.SetSingleLine(true);
        _port.SetTextColor(global::Android.Graphics.Color.White);
        _port.SetHintTextColor(global::Android.Graphics.Color.LightGray);
        fields.AddView(_port, new global::Android.Widget.LinearLayout.LayoutParams(
            0, Dp(58), 1.3f));
        layout.AddView(fields);

        _safeGraphics = new global::Android.Widget.CheckBox(this)
        {
            Text = "Graficos seguros (modo recomendado para celulares Mali/MediaTek)",
            TextSize = 12,
            Checked = prefs?.GetBoolean("safe_graphics", true) ?? true
        };
        _safeGraphics.SetTextColor(global::Android.Graphics.Color.Rgb(245, 213, 153));
        layout.AddView(_safeGraphics);

        _launch = StyledButton("INICIAR KABRA EN LAN", true);
        _launch.Click += async (_, _) => await StartLanClientAsync();
        layout.AddView(_launch, new global::Android.Widget.LinearLayout.LayoutParams(
            global::Android.Views.ViewGroup.LayoutParams.MatchParent, Dp(45)));

        _progress = new global::Android.Widget.ProgressBar(this)
        {
            Indeterminate = true,
            Visibility = ViewStates.Gone
        };
        layout.AddView(_progress, new global::Android.Widget.LinearLayout.LayoutParams(
            global::Android.Views.ViewGroup.LayoutParams.MatchParent, Dp(18)));
        _message = new global::Android.Widget.TextView(this)
        {
            Text = Intersect.Client.Core.AndroidBridge.PreviousSessionInterrupted
                ? "La sesion anterior no finalizo normalmente. Usa VER REGISTROS para revisar el ultimo paso."
                : "Preparado para conectar a la copia V48 por LAN.",
            TextSize = 12
        };
        _message.SetTextColor(global::Android.Graphics.Color.White);
        _message.SetMaxLines(5);
        layout.AddView(_message);

        var actions = new global::Android.Widget.LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        var showLog = StyledButton("VER REGISTROS");
        showLog.Click += (_, _) =>
        {
            var text = Intersect.Client.Core.AndroidBridge.GetDiagnosticReport();
            var dialog = new global::Android.App.AlertDialog.Builder(this);
            dialog.SetTitle("Kabra - ultimo inicio y errores");
            dialog.SetMessage(text);
            dialog.SetPositiveButton("COPIAR", (_, _) => CopyDiagnostic(text));
            dialog.SetNegativeButton("CERRAR", (_, _) => { });
            dialog.Show();
        };
        actions.AddView(showLog, new global::Android.Widget.LinearLayout.LayoutParams(
            0, Dp(42), 1f));
        var copyLog = StyledButton("COPIAR REGISTROS");
        copyLog.Click += (_, _) => CopyDiagnostic(
            Intersect.Client.Core.AndroidBridge.GetDiagnosticReport());
        actions.AddView(copyLog, new global::Android.Widget.LinearLayout.LayoutParams(
            0, Dp(42), 1f));
        layout.AddView(actions);
        SetContentView(scroll);
    }

    private void CopyDiagnostic(string contents)
    {
        try
        {
            var clipboard = GetSystemService(ClipboardService)
                as global::Android.Content.ClipboardManager
                ?? throw new InvalidOperationException("CLIPBOARD_UNAVAILABLE");
            clipboard.PrimaryClip = global::Android.Content.ClipData.NewPlainText(
                "Kabra Android diagnostico", contents);
            ShowStatus("Registro copiado. Pegalo en ChatGPT para revisar el cierre.");
        }
        catch (Exception ex) { ShowStatus("No se pudo copiar: " + ex.Message, true); }
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
            Intersect.Client.Core.AndroidBridge.Report("ANDROID_DEVICE_INFO: " +
                global::Android.OS.Build.Manufacturer + " " +
                global::Android.OS.Build.Model + " API=" +
                global::Android.OS.Build.VERSION.SdkInt);
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

            var safeGraphics = _safeGraphics?.Checked ?? true;
            // Default-safe: render on Android's UI looper to avoid GL context
            // ownership errors on some GLES drivers. Alternate runs on worker.
            RenderOnUIThread = safeGraphics;
            Intersect.Client.Core.AndroidBridge.Report(safeGraphics
                ? "ANDROID_RENDER_MODE_UI_SAFE"
                : "ANDROID_RENDER_MODE_WORKER_ALTERNATE");
            GetSharedPreferences(PrefsFile, FileCreationMode.Private)?
                .Edit()?.PutString("host", host)?.PutString("port", port.ToString())?
                .PutBoolean("safe_graphics", safeGraphics)?.Apply();
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
            // The game loop runs asynchronously. A successful Game.Run() is
            // not evidence that a frame was drawn. Surface a readable dialog
            // if the first frame never renders, rather than an endless black UI.
            _ = CheckFirstFrameAsync();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("KabraLAN", ex.ToString());
            Intersect.Client.Core.AndroidBridge.ReportFatal("LAN_START_FAILED", ex);
            ShowStatus("No se pudo iniciar Kabra: " + ex.GetType().Name + ": " + ex.Message, true);
            SetBusy(false);
        }
    }

    private async Task CheckFirstFrameAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(25));
            if (IsFinishing || IsDestroyed ||
                Intersect.Client.Core.AndroidBridge.FirstRenderSeen) return;
            var lastStatus = "BLACK_SCREEN_WATCHDOG: update=" +
                Intersect.Client.Core.AndroidBridge.FirstUpdateSeen +
                ", draw=" + Intersect.Client.Core.AndroidBridge.FirstDrawSeen +
                ", rendered=" + Intersect.Client.Core.AndroidBridge.FirstRenderSeen;
            Intersect.Client.Core.AndroidBridge.Report(lastStatus);
            RunOnUiThread(() =>
            {
                try
                {
                    if (IsFinishing || IsDestroyed) return;
                    var dialog = new global::Android.App.AlertDialog.Builder(this);
                    dialog.SetTitle("Kabra: no se pudo mostrar el juego");
                    dialog.SetMessage("La vista de Android se inicio pero aun no mostro " +
                        "un fotograma del RPG.\n" + lastStatus +
                        "\nNo es un error de la IP LAN. Copia el registro para revisar " +
                        "donde se detuvo el motor.");
                    dialog.SetPositiveButton("COPIAR REGISTROS", (_, _) =>
                        CopyDiagnostic(Intersect.Client.Core.AndroidBridge.GetDiagnosticReport()));
                    dialog.SetNegativeButton("CERRAR", (_, _) => { });
                    dialog.Show();
                }
                catch (Exception ex)
                {
                    Intersect.Client.Core.AndroidBridge.ReportFatal(
                        "BLACK_SCREEN_DIAGNOSTIC_DIALOG_FAILED", ex);
                }
            });
        }
        catch (Exception ex)
        {
            Intersect.Client.Core.AndroidBridge.ReportFatal("BLACK_SCREEN_WATCHDOG_FAILED", ex);
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