"""Android-only package adjustments for pinned Intersect .339."""
from pathlib import Path
import sys
root=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else Path("engine").resolve()
p=root/"Intersect.Client.Core"/"Intersect.Client.Core.csproj"
s=p.read_text(encoding="utf-8-sig")
s=s.replace('Include="MonoGame.Framework.DesktopGL"', 'Include="MonoGame.Framework.DesktopGL" Condition="\'$(KabraAndroidPort)\' != \'true\'"')
s=s.replace('Include="MonoGame.Library.SDL"', 'Include="MonoGame.Library.SDL" Condition="\'$(KabraAndroidPort)\' != \'true\'"')
s=s.replace('    <PackageReference Include="Steamworks.NET"', '    <PackageReference Include="MonoGame.Framework.Android" Version="3.8.1.303" Condition="\'$(KabraAndroidPort)\' == \'true\'" />\n    <PackageReference Include="Steamworks.NET"')
s=s.replace('<ItemGroup Label="Package Embedded Resources">','<ItemGroup Label="Package Embedded Resources" Condition="\'$(KabraAndroidPort)\' != \'true\'">')
s=s.replace('<EmbeddedResource Include="Resources\\openal32.dll" />','<EmbeddedResource Include="Resources\\openal32.dll" Condition="\'$(KabraAndroidPort)\' != \'true\'" />')
p.write_text(s,encoding="utf-8")
print("Android conditional package changes applied",p)