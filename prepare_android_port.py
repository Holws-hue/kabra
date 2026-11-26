"""Apply *Android-only* changes to pinned Intersect 0.8.0.339.
Fails loudly if upstream source layout changes. Never edits the Windows project in Git.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path("engine").resolve()
base = Path(__file__).resolve().parent / "Kabra.Android" / "EnginePatches"

def replace_once(path, old, new):
    text = path.read_text(encoding="utf-8-sig")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"PORT_PATCH_ANCHOR_MISMATCH: {path} expected 1, got {count}: {old[:95]}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")

proj = root / "Intersect.Client.Core" / "Intersect.Client.Core.csproj"
replace_once(proj, "<AllowUnsafeBlocks>true</AllowUnsafeBlocks>",
"""<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TargetFramework Condition="'$(KabraAndroidPort)' == 'true'">net8.0-android</TargetFramework>
    <RuntimeIdentifiers Condition="'$(KabraAndroidPort)' == 'true'">android-arm64</RuntimeIdentifiers>
    <DefineConstants Condition="'$(KabraAndroidPort)' == 'true'">$(DefineConstants);ANDROID</DefineConstants>""")
replace_once(proj, '<PackageReference Include="MonoGame.Framework.DesktopGL" Version="3.8.1.303" GeneratePathProperty="True" />',
"""<PackageReference Include="MonoGame.Framework.DesktopGL" Version="3.8.1.303" GeneratePathProperty="True" Condition="'$(KabraAndroidPort)' != 'true'" />
    <PackageReference Include="MonoGame.Framework.Android" Version="3.8.1.303" Condition="'$(KabraAndroidPort)' == 'true'" />""")
replace_once(proj, '<PackageReference Include="MonoGame.Library.SDL" Version="2.26.5.5" GeneratePathProperty="True" />',
"""<PackageReference Include="MonoGame.Library.SDL" Version="2.26.5.5" GeneratePathProperty="True" Condition="'$(KabraAndroidPort)' != 'true'" />""")
# DesktopGL brought NVorbis transitively; Android MonoGame doesn't.
replace_once(proj, '<PackageReference Include="Steamworks.NET" Version="20.1.0" />',
"""<PackageReference Include="Steamworks.NET" Version="20.1.0" />
    <PackageReference Include="NVorbis" Version="0.10.5" Condition="'$(KabraAndroidPort)' == 'true'" />""")
# Make the client's embedded handshake key use the verified Windows V48 public key.
# Explicit resource name is required by MonoSocket.GetManifestResourceStream.
replace_once(proj, '<EmbeddedResource Include="$(NetworkHandshakePublicKey)" />',
"""<EmbeddedResource Include="$(NetworkHandshakePublicKey)" LogicalName="Intersect.Client.network.handshake.bkey.pub" />""")
replace_once(proj, '<ItemGroup Label="Package Embedded Resources">', """<ItemGroup Label="Package Embedded Resources" Condition="'$(KabraAndroidPort)' != 'true'">""")
replace_once(proj, '<EmbeddedResource Include="Resources\\MonoGame.Framework.dll.config" />',
"""<EmbeddedResource Include="Resources\\MonoGame.Framework.dll.config" Condition="'$(KabraAndroidPort)' != 'true'" />""")
replace_once(proj, '<EmbeddedResource Include="Resources\\openal32.dll" />',
"""<EmbeddedResource Include="Resources\\openal32.dll" Condition="'$(KabraAndroidPort)' != 'true'" />""")
# JsonDatabase is in Client.Framework, not Client.Core. Its desktop preference path
# assumes Assembly.GetEntryAssembly() always exists; on .NET Android that can be null.
# Use our writable app-private working directory instead, without modifying desktop
# source, which is cloned temporarily into the Android CI runner.
json_db = root / "Intersect.Client.Framework" / "Database" / "JsonDatabase.cs"
replace_once(json_db,
    '''        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ".intersect",
            Assembly.GetEntryAssembly().GetName().Name, $"{instance.Host}.{instance.Port}.json");''',
    '''        // Android's managed entry assembly can be null. Keep preferences in the
        // app-private game directory selected by the AndroidBridge bootstrap.
        var preferencesRoot = OperatingSystem.IsAndroid()
            ? Path.Combine(Environment.CurrentDirectory, "preferences")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ".intersect",
                Assembly.GetEntryAssembly()?.GetName().Name ?? "Intersect");
        return Path.Combine(preferencesRoot, $"{instance.Host}.{instance.Port}.json");''')
game = root / "Intersect.Client.Core" / "MonoGame" / "IntersectGame.cs"
replace_once(game, 'Window.IsBorderless = Context.StartupOptions.BorderlessWindow;',
"""#if !ANDROID
        Window.IsBorderless = Context.StartupOptions.BorderlessWindow;
#endif""")
replace_once(game, 'Window.Position = new Microsoft.Xna.Framework.Point(',
"""#if !ANDROID
        Window.Position = new Microsoft.Xna.Framework.Point(""")
replace_once(game, 'Window.AllowAltF4 = false;',
"""Window.AllowAltF4 = false;
#endif""")
replace_once(game, 'if (Steam.SteamDeck)', 'if (!OperatingSystem.IsAndroid() && Steam.SteamDeck)')
replace_once(game, 'using var game = new IntersectGame(context, postStartupAction);\n            game.Run();',
"""#if ANDROID
            // Android MonoGame requires the Activity UI thread for Game constructor,
            // View binding and Game.Run. Bootstrapper itself may start on a worker.
            // Android Game.Run is asynchronous: retain the Game through AndroidBridge.
            Intersect.Client.Core.AndroidBridge.RunGameOnUiThread(() =>
            {
                Intersect.Client.Core.AndroidBridge.Report("CREATING_INTERSECT_GAME");
                var game = new IntersectGame(context, postStartupAction);
                Intersect.Client.Core.AndroidBridge.Report("INTERSECT_GAME_CREATED");
                Intersect.Client.Core.AndroidBridge.AttachGame(game);
                Intersect.Client.Core.AndroidBridge.Report("STARTING_GAME_RUN");
                game.Run();
                // AndroidGamePlatform.BeforeRun starts its worker but returns false.
                // The Activity was resumed before this late LAN launch, so the
                // game never received AndroidGameActivity.Resumed. Wake its GL view.
                Intersect.Client.Core.AndroidBridge.Report("GAME_RUN_RETURNED_ASYNC");
                Intersect.Client.Core.AndroidBridge.ResumeGameView(game);
            });
#else
            using var game = new IntersectGame(context, postStartupAction);
            game.Run();
#endif""")
# Android's AOT/linker must not remove a platform runner instantiated only via reflection.
client_context = root / "Intersect.Client.Core" / "Core" / "ClientContext.cs"
replace_once(client_context,
    'PlatformRunner = typeof(ClientContext).Assembly.CreateInstanceOf<IPlatformRunner>();',
"""#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("CREATING_PLATFORM_RUNNER");
        PlatformRunner = new Intersect.Client.MonoGame.IntersectGame.MonoGameRunner();
#else
        PlatformRunner = typeof(ClientContext).Assembly.CreateInstanceOf<IPlatformRunner>();
#endif""")
# Bootstrap stage markers identify where the native start gets stuck.
bootstrap = root / "Intersect.Client.Core" / "Core" / "Bootstrapper.cs"
replace_once(bootstrap, 'var clientConfiguration = ClientConfiguration.LoadAndSave();',
"""#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("LOADING_CLIENT_CONFIG");
#endif
        var clientConfiguration = ClientConfiguration.LoadAndSave();""")
replace_once(bootstrap, 'context.Start();',
"""#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("STARTING_CLIENT_CONTEXT");
#endif
        context.Start();""")
replace_once(game, 'Strings.Load();',
"""#if ANDROID
            Intersect.Client.Core.AndroidBridge.Report("LOADING_STRINGS");
#endif
            Strings.Load();""")
replace_once(game, 'mGraphics = new GraphicsDeviceManager(this)',
"""#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("CREATING_GRAPHICS_MANAGER");
#endif
        mGraphics = new GraphicsDeviceManager(this)""")
# Desktop requests MSAA x8; some GLES Android devices don't expose that sample count.
# Keep Windows rendering untouched and start with supported no-MSAA Android defaults.
replace_once(game, 'PreferMultiSampling = true,',
"""#if ANDROID
            PreferMultiSampling = false,
#else
            PreferMultiSampling = true,
#endif""")
replace_once(game, 'args.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = 8;',
"""#if ANDROID
            args.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = 0;
#else
            args.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = 8;
#endif""")
replace_once(game, 'Globals.ContentManager = new MonoContentManager();',
"""#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("CREATING_CONTENT_MANAGER");
#endif
        Globals.ContentManager = new MonoContentManager();""")
# ClientContext uses AsyncLocal<IApplicationContext>. Android's UI/GL events may
# run on a different ExecutionContext from the Bootstrapper worker.
replace_once(game, '        Context = context;\n        PostStartupAction = postStartupAction;',
"""        Context = context;
        PostStartupAction = postStartupAction;
#if ANDROID
        ApplicationContext.Context.Value = context;
        Intersect.Client.Core.AndroidBridge.Report("ANDROID_CLIENT_CONTEXT_UI_BOUND");
#endif""")
replace_once(game, 'Globals.Database.LoadPreferences();',
"""#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("LOADING_PREFERENCES");
#endif
        Globals.Database.LoadPreferences();""")
replace_once(game, 'protected override void Initialize()\n    {\n        base.Initialize();',
"""protected override void Initialize()
    {
#if ANDROID
        ApplicationContext.Context.Value = Context;
        Intersect.Client.Core.AndroidBridge.Report("GAME_INITIALIZE");
#endif
        base.Initialize();""")
# Set per-thread context before UI/GL callbacks, without modifying Windows behavior.
replace_once(game, '    protected override void Update(GameTime gameTime)\n    {',
"""    protected override void Update(GameTime gameTime)
    {
#if ANDROID
        ApplicationContext.Context.Value = Context;
        Intersect.Client.Core.AndroidBridge.DrainQueuedGameInput();
#endif""")
replace_once(game, '    protected override void Draw(GameTime gameTime)\n    {',
"""    protected override void Draw(GameTime gameTime)
    {
#if ANDROID
        ApplicationContext.Context.Value = Context;
#endif""")
replace_once(game, '    protected override void LoadContent()\n    {',
"""    protected override void LoadContent()
    {
#if ANDROID
        ApplicationContext.Context.Value = Context;
#endif""")
replace_once(game, 'private void IntersectInit()\n    {',
"""private void IntersectInit()
    {
#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("INTERSECT_INIT");
#endif""")
replace_once(game, 'mInitialized = true;',
"""mInitialized = true;
#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("INTERSECT_INITIALIZED");
#endif""")
# Black-screen troubleshooting: determine if MonoGame actually starts callbacks,
# creates the menu and submits a successful first graphical render.
replace_once(game, '    private bool mInitialized;',
"""    private bool mInitialized;
#if ANDROID
    private bool _androidLoggedFirstUpdate;
    private bool _androidLoggedFirstDraw;
    private bool _androidLoggedFirstRender;
#endif""")
replace_once(game, '    protected override void LoadContent()\n    {\n#if ANDROID\n        ApplicationContext.Context.Value = Context;\n#endif',
"""    protected override void LoadContent()
    {
#if ANDROID
        ApplicationContext.Context.Value = Context;
        Intersect.Client.Core.AndroidBridge.Report("GAME_LOAD_CONTENT_BEGIN");
#endif""")
replace_once(game, '        base.LoadContent();',
"""        base.LoadContent();
#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("GAME_LOAD_CONTENT_DONE");
#endif""")
replace_once(game, '        Intersect.Client.Core.AndroidBridge.DrainQueuedGameInput();',
"""        Intersect.Client.Core.AndroidBridge.DrainQueuedGameInput();
        Intersect.Client.Core.AndroidBridge.UpdateMobileControls(
            Globals.GameState == GameStates.InGame);
        if (!_androidLoggedFirstUpdate)
        {
            _androidLoggedFirstUpdate = true;
            Intersect.Client.Core.AndroidBridge.Report("GAME_FIRST_UPDATE");
        }""")
replace_once(game, '    protected override void Draw(GameTime gameTime)\n    {\n#if ANDROID\n        ApplicationContext.Context.Value = Context;\n#endif',
"""    protected override void Draw(GameTime gameTime)
    {
#if ANDROID
        ApplicationContext.Context.Value = Context;
        if (!_androidLoggedFirstDraw)
        {
            _androidLoggedFirstDraw = true;
            Intersect.Client.Core.AndroidBridge.Report("GAME_FIRST_DRAW");
        }
#endif""")
replace_once(game, '        Main.Start(Context);',
"""#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("MAIN_START_BEGIN");
#endif
        Main.Start(Context);
#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("MAIN_START_DONE");
#endif""")
replace_once(game, '                    Core.Graphics.Render(gameTime.ElapsedGameTime, gameTime.TotalGameTime);',
"""                    Core.Graphics.Render(gameTime.ElapsedGameTime, gameTime.TotalGameTime);
#if ANDROID
                    if (!_androidLoggedFirstRender)
                    {
                        _androidLoggedFirstRender = true;
                        Intersect.Client.Core.AndroidBridge.Report("GAME_FIRST_RENDER_SUCCESS");
                    }
#endif""")
# Crash diagnostics: MonoGame's Android frame dispatcher can swallow exceptions
# on its own GL worker, so persist exceptions at the actual game callbacks.
# Keep all guards Android-only, preserving the pinned Windows client behavior.
replace_once(game, '        base.Initialize();\n\n        if (mUpdater != null)',
"""#if ANDROID
        try
        {
#endif
        base.Initialize();

        if (mUpdater != null)""")
replace_once(game, '        mGraphics.ApplyChanges();\n    }\n\n    private void IntersectInit()',
"""        mGraphics.ApplyChanges();
#if ANDROID
        }
        catch (Exception ex)
        {
            Intersect.Client.Core.AndroidBridge.ReportFatal("GAME_INITIALIZE_FAILED", ex);
            throw;
        }
#endif
    }

    private void IntersectInit()""")
replace_once(game, '        if (mUpdater != null)\n        {\n            if (mUpdater.CheckUpdaterContentLoaded())',
"""#if ANDROID
        try
        {
#endif
        if (mUpdater != null)
        {
            if (mUpdater.CheckUpdaterContentLoaded())""")
replace_once(game, '        base.Update(gameTime);\n    }\n\n    protected override void LoadContent()',
"""        base.Update(gameTime);
#if ANDROID
        }
        catch (Exception ex)
        {
            Intersect.Client.Core.AndroidBridge.ReportFatal("GAME_UPDATE_FAILED", ex);
            throw;
        }
#endif
    }

    protected override void LoadContent()""")
replace_once(game, '        base.LoadContent();\n#if ANDROID\n        Intersect.Client.Core.AndroidBridge.Report("GAME_LOAD_CONTENT_DONE");\n#endif',
"""#if ANDROID
        try
        {
#endif
        base.LoadContent();
#if ANDROID
        Intersect.Client.Core.AndroidBridge.Report("GAME_LOAD_CONTENT_DONE");
#endif""")
replace_once(game, '            LoadUpdaterContent();\n        }\n    }\n\n    /// <summary>\n    ///     This is called when the game should draw itself.',
"""            LoadUpdaterContent();
        }
#if ANDROID
        }
        catch (Exception ex)
        {
            Intersect.Client.Core.AndroidBridge.ReportFatal("GAME_LOAD_CONTENT_FAILED", ex);
            throw;
        }
#endif
    }

    /// <summary>
    ///     This is called when the game should draw itself.""")
replace_once(game, '        GraphicsDevice.BlendState = BlendState.NonPremultiplied;',
"""#if ANDROID
        try
        {
#endif
        GraphicsDevice.BlendState = BlendState.NonPremultiplied;""")
replace_once(game, '        base.Draw(gameTime);\n    }\n\n    protected override void OnExiting',
"""        base.Draw(gameTime);
#if ANDROID
        }
        catch (Exception ex)
        {
            Intersect.Client.Core.AndroidBridge.ReportFatal("GAME_DRAW_FAILED", ex);
            throw;
        }
#endif
    }

    protected override void OnExiting""")
# Avoid desktop SDL completely on Android: the previous patch only hid the
# Window.Position assignment, yet still called Sdl2.GetDisplayBounds(),
# which requires SDL and can crash when the renderer initializes.
renderer = root / "Intersect.Client.Core" / "MonoGame" / "Graphics" / "MonoRenderer.cs"
replace_once(renderer, '        if (fsChanged || initial)\n        {\n            var displayBounds = Sdl2.GetDisplayBounds();',
"""#if !ANDROID
        if (fsChanged || initial)
        {
            var displayBounds = Sdl2.GetDisplayBounds();""")
replace_once(renderer, '                currentDisplayBounds.y + (currentDisplayBounds.h - mScreenHeight) / 2\n            );\n        }\n\n        mOldDisplayMode',
"""                currentDisplayBounds.y + (currentDisplayBounds.h - mScreenHeight) / 2
            );
        }
#endif

        mOldDisplayMode""")
# On narrow/rotated Android surfaces the desktop 800x600 minimum can produce
# an empty resolution list, then the original renderer indexes [0].
replace_once(renderer, '        return _validVideoModes;',
"""#if ANDROID
        if (_validVideoModes.Count == 0)
            _validVideoModes.Add("800x480");
#endif
        return _validVideoModes;""")
input_cs = root / "Intersect.Client.Core" / "MonoGame" / "Input" / "MonoInput.cs"
# Intersect login and registration invoke the inputHandler overload; desktop
# Steam Deck keyboard is not available on Android.
replace_once(input_cs,
    '        if (!Steam.SteamDeck)\n        {\n            return;\n        }',
"""#if ANDROID
        Intersect.Client.Core.AndroidBridge.OpenNativeKeyboard(
            description, text, keyboardType == KeyboardType.Password, maxLength, inputHandler);
        return;
#else
        if (!Steam.SteamDeck)
        {
            return;
        }
#endif""")
replace_once(input_cs, 'myGame.Window.TextInput += Window_TextInput;',
"""#if !ANDROID
        myGame.Window.TextInput += Window_TextInput;
#endif""")
replace_once(input_cs, 'var mouseState = Mouse.GetState();\n\n            if (mouseState.X',
"""var mouseState = Mouse.GetState();
#if ANDROID
            var touches = Microsoft.Xna.Framework.Input.Touch.TouchPanel.GetState();
            var viewport = mMyGame.GraphicsDevice.Viewport;
            mouseState = AndroidTouchInput.ApplyPointer(
                mouseState, touches, viewport.Width, viewport.Height,
                Globals.GameState == GameStates.InGame);
            keyboardState = AndroidTouchInput.ApplyMovement(
                keyboardState, touches, viewport.Width, viewport.Height,
                Globals.GameState == GameStates.InGame);
#endif

            if (mouseState.X""")
# Android.Resource shadows the entity type in PacketHandler; force Intersect's full name.
packet_handler = root / "Intersect.Client.Core" / "Networking" / "PacketHandler.cs"
replace_once(packet_handler, 'var entity = new Resource(packet.EntityId, packet);',
"""var entity = new Intersect.Client.Entities.Resource(packet.EntityId, packet);""")
# Map the unmodified core's assembly access via a bridge inside Client.Core.
for name, dest in [
    ("AndroidBridge.cs", root / "Intersect.Client.Core" / "Core" / "AndroidBridge.cs"),
    ("AndroidTouchInput.cs", root / "Intersect.Client.Core" / "MonoGame" / "Input" / "AndroidTouchInput.cs"),
    ("AndroidGlobalAliases.cs", root / "Intersect.Client.Core" / "Core" / "AndroidGlobalAliases.cs"),
]:
    dest.write_bytes((base / name).read_bytes())
print("ANDROID_SOURCE_PATCH_APPLIED", root)
