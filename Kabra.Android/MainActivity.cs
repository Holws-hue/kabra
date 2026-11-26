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

/// <summary>LAN-only pre-login screen: launch the real Intersect client only after valid IPv4/port.</summary>
[Activity(Label = "Kabra Online 2D LAN", MainLauncher = true,
 ScreenOrientation = ScreenOrientation.Landscape,
 ConfigurationChanges = ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
 ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : AndroidGameActivity
{
    private const string PrefsFile = "kabra_lan";
    private global::Android.Widget.EditText? _host;
    private global::Android.Widget.EditText? _port;
    private global::Android.Widget.TextView? _error;
    private bool _started;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ShowLanSetup();
    }

    private void ShowLanSetup()
    {
        var prefs = GetSharedPreferences(PrefsFile, FileCreationMode.Private);
        var layout = new global::Android.Widget.LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        layout.SetPadding(32, 24, 32, 16);
        layout.SetBackgroundColor(global::Android.Graphics.Color.Rgb(19, 20, 27));
        var scroll = new global::Android.Widget.ScrollView(this);
        scroll.AddView(layout);
        var heading = new global::Android.Widget.TextView(this) { Text = "KABRA ONLINE 2D - CONEXION LAN", TextSize = 23 };
        heading.SetTextColor(global::Android.Graphics.Color.White);
        layout.AddView(heading);
        var help = new global::Android.Widget.TextView(this)
        {
            Text = "Conecta el celular y la PC al mismo Wi-Fi. Ingresa la IPv4 privada de la PC y el puerto UDP de tu COPIA V48. No usa servidores publicos.",
            TextSize = 15
        };
        help.SetTextColor(global::Android.Graphics.Color.LightGray);
        layout.AddView(help);
        _host = new global::Android.Widget.EditText(this)
        {
            Hint = "IPv4 PC (ej.: 192.168.1.15)",
            Text = prefs?.GetString("host", string.Empty) ?? string.Empty,
            InputType = global::Android.Text.InputTypes.ClassPhone
        };
        layout.AddView(_host);
        _port = new global::Android.Widget.EditText(this)
        {
            Hint = "Puerto UDP de la copia V48",
            Text = prefs?.GetString("port", string.Empty) ?? string.Empty,
            InputType = global::Android.Text.InputTypes.ClassNumber
        };
        layout.AddView(_port);
        _error = new global::Android.Widget.TextView(this) { TextSize = 14 };
        _error.SetTextColor(global::Android.Graphics.Color.Rgb(255, 140, 140));
        layout.AddView(_error);
        var launch = new global::Android.Widget.Button(this) { Text = "GUARDAR Y JUGAR EN LAN" };
        launch.Click += (_, _) => StartLanClient();
        layout.AddView(launch);
        SetContentView(scroll);
    }

    private void StartLanClient()
    {
        if (_started) return;
        var host = _host?.Text?.Trim() ?? string.Empty;
        var portText = _port?.Text?.Trim() ?? string.Empty;
        if (!IsPrivateIpv4(host) || !ushort.TryParse(portText, out var port) || port == 0)
        {
            if (_error != null) _error.Text = "Usa una IPv4 LAN privada y un puerto entre 1 y 65535.";
            return;
        }
        _started = true;
        try
        {
            var root = Path.Combine(FilesDir?.AbsolutePath ??
                throw new InvalidOperationException("Android FilesDir unavailable"), "kabra");
            Directory.CreateDirectory(root);
            CopyAssets("resources", root);
            var configPath = Path.Combine(root, "resources", "config.json");
            var config = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ??
                throw new InvalidOperationException("KABRA_CONFIG_INVALID");
            config["Host"] = host;
            config["Port"] = (int)port;
            config["UpdateUrl"] = ""; // disable Internet updater
            File.WriteAllText(configPath, config.ToJsonString());
            GetSharedPreferences(PrefsFile, FileCreationMode.Private)?.Edit()
                ?.PutString("host", host)?.PutString("port", port.ToString())?.Apply();
            // Launch Intersect, not an independent demo or mock menu.
            Intersect.Client.Core.AndroidBridge.Start(this, root);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("KabraLAN", ex.ToString());
            _started = false;
            if (_error != null) _error.Text = "Error al iniciar: " + ex.Message;
        }
    }

    private static bool IsPrivateIpv4(string text)
    {
        if (!IPAddress.TryParse(text, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        // Reject alternate spellings, hostnames, loopback, broadcast and WAN endpoints.
        if (!string.Equals(ip.ToString(), text, StringComparison.Ordinal)) return false;
        var b = ip.GetAddressBytes();
        if (b[3] == 0 || b[3] == 255) return false;
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    private void CopyAssets(string folder, string destination)
    {
        var names = Assets?.List(folder) ??
            throw new InvalidOperationException("Android AssetManager unavailable");
        if (names.Length == 0)
            throw new InvalidOperationException("KABRA_ASSETS_MISSING: " + folder);
        foreach (var name in names)
        {
            var child = folder + "/" + name;
            var children = Assets!.List(child);
            if (children is { Length: > 0 })
            {
                CopyAssets(child, destination);
                continue;
            }
            var targetFile = Path.Combine(destination, child);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            if (File.Exists(targetFile)) continue;
            using var src = Assets.Open(child);
            using var dst = File.Create(targetFile);
            src.CopyTo(dst);
        }
    }
}