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
replace_once(proj, '<ItemGroup Label="Package Embedded Resources">', """<ItemGroup Label="Package Embedded Resources" Condition="'$(KabraAndroidPort)' != 'true'">""")
replace_once(proj, '<EmbeddedResource Include="Resources\\MonoGame.Framework.dll.config" />',
"""<EmbeddedResource Include="Resources\\MonoGame.Framework.dll.config" Condition="'$(KabraAndroidPort)' != 'true'" />""")
replace_once(proj, '<EmbeddedResource Include="Resources\\openal32.dll" />',
"""<EmbeddedResource Include="Resources\\openal32.dll" Condition="'$(KabraAndroidPort)' != 'true'" />""")
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
"""using var game = new IntersectGame(context, postStartupAction);
#if ANDROID
            Intersect.Client.Core.AndroidBridge.AttachGame(game);
#endif
            game.Run();""")
# Android GameWindow does not expose position or SDL desktop text input.
renderer = root / "Intersect.Client.Core" / "MonoGame" / "Graphics" / "MonoRenderer.cs"
replace_once(renderer, 'mGameWindow.Position = new Microsoft.Xna.Framework.Point(',
"""#if !ANDROID
            mGameWindow.Position = new Microsoft.Xna.Framework.Point(""")
replace_once(renderer, 'currentDisplayBounds.y + (currentDisplayBounds.h - mScreenHeight) / 2\n            );',
"""currentDisplayBounds.y + (currentDisplayBounds.h - mScreenHeight) / 2
            );
#endif""")
input_cs = root / "Intersect.Client.Core" / "MonoGame" / "Input" / "MonoInput.cs"
replace_once(input_cs, 'myGame.Window.TextInput += Window_TextInput;',
"""#if !ANDROID
        myGame.Window.TextInput += Window_TextInput;
#endif""")
replace_once(input_cs, 'var mouseState = Mouse.GetState();\n\n            if (mouseState.X',
"""var mouseState = Mouse.GetState();
#if ANDROID
            var touches = Microsoft.Xna.Framework.Input.Touch.TouchPanel.GetState();
            var viewport = mMyGame.GraphicsDevice.Viewport;
            mouseState = AndroidTouchInput.ApplyPointer(mouseState, touches, viewport.Width, viewport.Height);
            keyboardState = AndroidTouchInput.ApplyMovement(keyboardState, touches, viewport.Width, viewport.Height);
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
