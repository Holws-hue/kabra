using Android.Views;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;

namespace Intersect.Client.Core;

/// <summary>
/// Android-specific bridge for the real Intersect client. Non-UI bootstrap work
/// may run in a worker, but MonoGame Game creation, view attachment and Run()
/// MUST execute on the Android UI thread.
/// </summary>
public static class AndroidBridge
{
    private static readonly object Sync = new();
    private static readonly ConcurrentQueue<Action> GameInputQueue = new();
    private static AndroidGameActivity? _activity;
    private static Game? _game;
    private static string? _logPath;
    private static string? _logRoot;
    private static string? _sessionMarker;
    private static Action<string>? _status;
    private static global::Android.Widget.TextView? _overlayStatus;


    public static Action<string>? StageChanged { get; set; }
    public static bool ViewAttached { get; private set; }

    /// <summary>Register logs without clearing evidence from the previous run.</summary>
    public static void ConfigureDiagnosticStorage(string root)
    {
        Directory.CreateDirectory(root);
        lock (Sync)
        {
            _logRoot = root;
            _logPath = Path.Combine(root, "kabra_android_boot.log");
            _sessionMarker = Path.Combine(root, "kabra_session_active.flag");
        }
    }

    public static string GetDiagnosticReport()
    {
        string? root;
        lock (Sync) root = _logRoot;
        if (root == null) return "No existe un directorio de registros.";
        var report = new System.Text.StringBuilder();
        foreach (var name in new[] {
            "last_crash.log", "kabra_previous_session.log", "kabra_android_boot.log", "last_exit_unknown.log"
        })
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            report.AppendLine("---- " + name + " ----");
            try
            {
                report.AppendLine(string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(100)));
            }
            catch (Exception ex) { report.AppendLine("READ_ERROR: " + ex.Message); }
        }
        if (report.Length == 0)
            report.AppendLine("Todavia no hay registros. Inicia Kabra al menos una vez.");
        return report.ToString();
    }

    public static bool PreviousSessionInterrupted
    {
        get
        {
            string? root;
            lock (Sync) root = _logRoot;
            return root != null && File.Exists(Path.Combine(root, "kabra_session_active.flag"));
        }
    }

    /// <summary>
    /// Save logs before overwriting them. A native SIGSEGV/driver crash may not
    /// invoke managed exception handlers; preserve the last stage independently.
    /// An unfinished marker means an unclean exit, not proof of a managed crash.
    /// </summary>
    public static void BeginDiagnostics(string writableGameRoot)
    {
        ConfigureDiagnosticStorage(writableGameRoot);
        lock (Sync)
        {
            if (_logPath == null || _sessionMarker == null) return;
            try
            {
                if (File.Exists(_logPath))
                {
                    File.Copy(_logPath, Path.Combine(writableGameRoot, "kabra_previous_session.log"), true);
                }
                if (File.Exists(_sessionMarker))
                {
                    File.WriteAllText(Path.Combine(writableGameRoot, "last_exit_unknown.log"),
                        DateTime.UtcNow.ToString("O") +
                        " | La ejecucion anterior termino sin cierre confirmado. Posible cierre forzado, " +
                        "error nativo o proceso eliminado por Android; leer kabra_previous_session.log." +
                        Environment.NewLine);
                }
                File.WriteAllText(_sessionMarker, DateTime.UtcNow.ToString("O"));
                File.WriteAllText(_logPath,
                    DateTime.UtcNow.ToString("O") + " | KABRA_ANDROID_LOG_STARTED" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("KabraBootstrap", "BOOT_HISTORY_WRITE_FAILED: " + ex);
            }
        }
        global::Android.Util.Log.Info("KabraBootstrap", "KABRA_ANDROID_LOG_STARTED");
    }

    public static void ReportFatal(string source, Exception exception)
    {
        var detail = source + ": " + exception;
        Persist("FATAL_MANAGED_EXCEPTION: " + detail);
        string? root;
        lock (Sync) root = _logRoot;
        if (root == null) return;
        try
        {
            File.AppendAllText(Path.Combine(root, "last_crash.log"),
                DateTime.UtcNow.ToString("O") + " | " + detail + Environment.NewLine);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("KabraBootstrap", "CRASH_LOG_FAILED: " + ex);
        }
    }

    /// <summary>Mark graceful stop. An unmarked stop leaves the diagnostic marker.</summary>
    public static void ReportCleanExit()
    {
        string? marker;
        lock (Sync) marker = _sessionMarker;
        try
        {
            if (marker != null && File.Exists(marker)) File.Delete(marker);
        }
        catch (Exception ex) { global::Android.Util.Log.Warn("KabraBootstrap", ex.ToString()); }
    }

    private static void Persist(string stage)
    {
        global::Android.Util.Log.Info("KabraBootstrap", stage);
        lock (Sync)
        {
            if (_logPath == null) return;
            try
            {
                File.AppendAllText(_logPath,
                    DateTime.UtcNow.ToString("O") + " | " + stage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("KabraBootstrap", "BOOT_LOG_WRITE_FAILED: " + ex);
            }
        }
    }

    public static void Report(string stage)
    {
        Persist(stage);
        AndroidGameActivity? activity;
        global::Android.Widget.TextView? label;
        lock (Sync) { activity = _activity; label = _overlayStatus; }
        if (activity != null && label != null)
        {
            activity.RunOnUiThread(() => label.Text = "KABRA: " + stage);
        }
        Action<string>? callback;
        lock (Sync) callback = _status;
        try { callback?.Invoke(stage); }
        catch (Exception ex) { Persist("BOOT_STATUS_CALLBACK_FAILED: " + ex.Message); }
        try { StageChanged?.Invoke(stage); }
        catch (Exception ex) { Persist("BOOT_STAGE_CALLBACK_FAILED: " + ex.Message); }
    }

    public static Task StartAsync(AndroidGameActivity activity, string writableGameRoot, Action<string> status)
    {
        if (activity == null) throw new ArgumentNullException(nameof(activity));
        if (status == null) throw new ArgumentNullException(nameof(status));
        lock (Sync)
        {
            _activity = activity;
            _status = status;
            _game = null;
            ViewAttached = false;
        }
        if (_logPath == null) BeginDiagnostics(writableGameRoot);
        return Task.Run(() =>
        {
            try
            {
                Report("ANDROID_BOOTSTRAP_BEGIN");
                Environment.CurrentDirectory = writableGameRoot;
                Report("ANDROID_BOOTSTRAP_WORKDIR_OK");
                Bootstrapper.Start();
                if (!ViewAttached)
                {
                    Report("ANDROID_BOOTSTRAP_RETURNED_WITHOUT_GAME_VIEW");
                    throw new InvalidOperationException(
                        "INTERSECT_STARTUP_RETURNED_WITHOUT_GAME_VIEW: inspect prior boot stages");
                }
                Report("ANDROID_BOOTSTRAP_RETURNED_GAME_ATTACHED");
            }
            catch (Exception ex)
            {
                ReportFatal("ANDROID_BOOTSTRAP_EXCEPTION", ex);
                Report("ANDROID_BOOTSTRAP_FAILED: " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
        });
    }

    /// <summary>
    /// Called by the Android-only patch inside IntersectGame.MonoGameRunner.Start.
    /// Do not create the Game in Task.Run: Android's MonoGame GamePlatform and
    /// GL view depend on the Activity's main looper.
    /// </summary>
    public static void RunGameOnUiThread(Action createAttachAndRun)
    {
        if (createAttachAndRun == null) throw new ArgumentNullException(nameof(createAttachAndRun));
        AndroidGameActivity activity;
        lock (Sync)
            activity = _activity ?? throw new InvalidOperationException("ANDROID_ACTIVITY_NOT_READY");

        if (global::Android.OS.Looper.MyLooper() == global::Android.OS.Looper.MainLooper)
        {
            Report("ANDROID_GAME_UI_THREAD_CONFIRMED");
            createAttachAndRun();
            return;
        }

        Report("ANDROID_GAME_UI_DISPATCH_QUEUED");
        using var completed = new ManualResetEventSlim(false);
        Exception? failure = null;
        activity.RunOnUiThread(() =>
        {
            try
            {
                Report("ANDROID_GAME_UI_THREAD_CONFIRMED");
                createAttachAndRun();
            }
            catch (Exception ex)
            {
                failure = ex;
                ReportFatal("ANDROID_GAME_UI_EXCEPTION", ex);
            }
            finally
            {
                completed.Set();
            }
        });

        if (!completed.Wait(TimeSpan.FromSeconds(45)))
        {
            Persist("ANDROID_GAME_UI_DISPATCH_TIMEOUT");
            throw new TimeoutException("ANDROID_GAME_UI_DISPATCH_TIMEOUT");
        }
        if (failure != null)
            throw new InvalidOperationException("ANDROID_GAME_UI_INIT_FAILED: " + failure.Message, failure);
    }

    internal static void AttachGame(Game game)
    {
        AndroidGameActivity activity;
        lock (Sync)
            activity = _activity ?? throw new InvalidOperationException("ANDROID_ACTIVITY_NOT_READY");

        Report("ANDROID_GAME_ATTACH_BEGIN");
        if (global::Android.OS.Looper.MyLooper() != global::Android.OS.Looper.MainLooper)
            throw new InvalidOperationException("ANDROID_GAME_ATTACH_WRONG_THREAD");

        var view = game.Services.GetService(typeof(View)) as View;
        if (view == null)
            throw new InvalidOperationException("KABRA_ANDROID_GAME_VIEW_MISSING");

        // SurfaceView and its diagnostics must remain in the Activity together.
        // The previous implementation replaced the whole LAN UI, hiding the log button
        // precisely when a black screen occurred.
        var frame = new global::Android.Widget.FrameLayout(activity);
        frame.AddView(view, new global::Android.Widget.FrameLayout.LayoutParams(
            global::Android.Views.ViewGroup.LayoutParams.MatchParent,
            global::Android.Views.ViewGroup.LayoutParams.MatchParent));

        var panel = new global::Android.Widget.LinearLayout(activity)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        panel.SetBackgroundColor(global::Android.Graphics.Color.Argb(205, 17, 20, 28));
        panel.SetPadding(10, 6, 10, 4);
        var status = new global::Android.Widget.TextView(activity)
        {
            Text = "KABRA: vista Android creada",
            TextSize = 11
        };
        status.SetTextColor(global::Android.Graphics.Color.White);
        status.SetMaxLines(2);
        panel.AddView(status);

        var diagnostics = new global::Android.Widget.Button(activity)
        {
            Text = "VER / COPIAR REGISTRO",
            TextSize = 11
        };
        diagnostics.Click += (_, _) => ShowDiagnostics(activity);
        panel.AddView(diagnostics);

        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        var widthPx = (int)(265 * density);
        var layout = new global::Android.Widget.FrameLayout.LayoutParams(
            widthPx, global::Android.Views.ViewGroup.LayoutParams.WrapContent,
            global::Android.Views.GravityFlags.Top | global::Android.Views.GravityFlags.Right);
        frame.AddView(panel, layout);
        activity.SetContentView(frame);
        lock (Sync)
        {
            _game = game; // Retain while Android runs the asynchronous game loop.
            _overlayStatus = status;
            ViewAttached = true;
        }
        Report("ANDROID_GAME_ATTACH_DONE");
    }

    private static void ShowDiagnostics(AndroidGameActivity activity)
    {
        try
        {
            var details = GetDiagnosticReport();
            var builder = new global::Android.App.AlertDialog.Builder(activity);
            builder.SetTitle("Kabra Android - registro");
            builder.SetMessage(details);
            builder.SetPositiveButton("COPIAR", (_, _) =>
            {
                var clipboard = activity.GetSystemService(global::Android.Content.Context.ClipboardService)
                    as global::Android.Content.ClipboardManager;
                if (clipboard == null) return;
                clipboard.PrimaryClip = global::Android.Content.ClipData.NewPlainText(
                    "Kabra Android log", details);
                global::Android.Util.Log.Info("KabraBootstrap", "Boot report copied to clipboard");
            });
            builder.SetNegativeButton("CERRAR", (_, _) => { });
            builder.Show();
        }
        catch (Exception ex)
        {
            Persist("ANDROID_DIAGNOSTIC_OVERLAY_FAILED: " + ex);
        }
    }

    /// <summary>
    /// The game is created AFTER Activity.OnResume, when the user submits the LAN
    /// address. MonoGame's AndroidGamePlatform.BeforeRun starts a worker and
    /// returns without calling StartRunLoop. Its game-view worker stays dormant
    /// until Resume() is signalled. The normal Activity.Resumed event already
    /// happened before Game existed, so explicitly resume this game view once.
    /// </summary>
    public static void ResumeGameView(Game game)
    {
        if (game == null) throw new ArgumentNullException(nameof(game));
        if (global::Android.OS.Looper.MyLooper() != global::Android.OS.Looper.MainLooper)
            throw new InvalidOperationException("ANDROID_GAME_RESUME_WRONG_THREAD");

        var view = game.Services.GetService(typeof(View)) as MonoGameAndroidGameView;
        if (view == null)
        {
            Report("ANDROID_GAME_RESUME_VIEW_MISSING");
            throw new InvalidOperationException("ANDROID_GAME_RESUME_VIEW_MISSING");
        }
        // The AndroidGamePlatform subscribes to AndroidGameActivity.Resumed
        // during Game creation. With this late LAN launch, that event has
        // already fired, so Platform.IsActive remains false (its default).
        // The view checks that flag before calling game.Tick(). Set it using
        // MonoGame's own property setter and fail visibly if API layout differs.
        if (!game.IsActive)
        {
            Report("ANDROID_GAME_PLATFORM_ACTIVATING");
            var platformField = typeof(Game).GetField("Platform",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            var platform = platformField?.GetValue(game) ??
                throw new InvalidOperationException("MONOGAME_PLATFORM_FIELD_NOT_FOUND");
            var activeProperty = platform.GetType().GetProperty("IsActive",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            var setActive = activeProperty?.GetSetMethod(nonPublic: true) ??
                throw new InvalidOperationException("MONOGAME_ACTIVE_SETTER_NOT_FOUND");
            setActive.Invoke(platform, new object[] { true });
        }
        if (!game.IsActive)
            throw new InvalidOperationException("MONOGAME_ACTIVITY_STILL_INACTIVE");

        Report("ANDROID_GAME_PLATFORM_ACTIVE");
        Report("ANDROID_GAME_VIEW_RESUME_BEGIN");
        view.Resume();
        Report("ANDROID_GAME_VIEW_RESUME_SIGNALLED");
    }

    /// <summary>
    /// The Intersect login Gwen input calls MonoInput.OpenKeyboard. Show an
    /// Android-native text/password dialog; never log the entered credentials.
    /// The resulting textbox callback is drained by IntersectGame.Update.
    /// </summary>
    public static void OpenNativeKeyboard(
        string title, string existingText, bool password, uint maxLength, Action<string?> inputHandler)
    {
        if (inputHandler == null) throw new ArgumentNullException(nameof(inputHandler));
        AndroidGameActivity activity;
        lock (Sync)
            activity = _activity ?? throw new InvalidOperationException("ANDROID_KEYBOARD_ACTIVITY_NOT_READY");

        Report("ANDROID_KEYBOARD_REQUESTED");
        activity.RunOnUiThread(() =>
        {
            try
            {
                var edit = new global::Android.Widget.EditText(activity)
                {
                    Text = existingText ?? string.Empty,
                    InputType = password
                        ? global::Android.Text.InputTypes.ClassText |
                          global::Android.Text.InputTypes.TextVariationPassword
                        : global::Android.Text.InputTypes.ClassText
                };
                var builder = new global::Android.App.AlertDialog.Builder(activity);
                builder.SetTitle(title ?? "Ingresar texto");
                builder.SetView(edit);
                builder.SetPositiveButton("Aceptar", (_, _) =>
                {
                    var value = edit.Text ?? string.Empty;
                    var length = (int)Math.Min(maxLength, 4096u);
                    if (value.Length > length) value = value[..length];
                    GameInputQueue.Enqueue(() => inputHandler(value));
                    Report("ANDROID_KEYBOARD_SUBMITTED");
                });
                builder.SetNegativeButton("Cancelar", (_, _) => Report("ANDROID_KEYBOARD_CANCELLED"));
                builder.Show();
                Report("ANDROID_KEYBOARD_VISIBLE");
            }
            catch (Exception ex)
            {
                Persist("ANDROID_KEYBOARD_FAILED: " + ex);
            }
        });
    }

    public static void DrainQueuedGameInput()
    {
        while (GameInputQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { ReportFatal("ANDROID_KEYBOARD_INPUT_CALLBACK_FAILED", ex); }
        }
    }

    public static void Stop()
    {
        Game? game;
        AndroidGameActivity? activity;
        lock (Sync)
        {
            game = _game;
            activity = _activity;
            _game = null;
            _activity = null;
            _status = null;
            _overlayStatus = null;
            ViewAttached = false;
        }
        Report("ANDROID_BRIDGE_STOP");
        if (game == null || activity == null) return;
        activity.RunOnUiThread(() =>
        {
            try { game.Exit(); }
            catch (Exception ex) { Persist("ANDROID_GAME_EXIT_FAILED: " + ex); }
        });
    }
}
