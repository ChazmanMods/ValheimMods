from pathlib import Path
p=Path('ArcaneDecorNorseFurnishings/Plugin.cs');s=p.read_text().replace('source.GetComponentInChildren<MeshRenderer>(true)','source.GetComponent<WearNTear>().m_new.GetComponentInChildren<MeshRenderer>(true)').replace('b.min.z+b.size.z*.04f','b.max.z-b.size.z*.04f')
s=s.replace('foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))','foreach(var f in root.GetComponent<WearNTear>().m_new.GetComponentsInChildren<MeshFilter>(true))')
p.write_text(s)
p=Path('ArcaneDecorNorseFurnishings/ArcaneDecorNorseFurnishings.csproj');s=p.read_text().replace('<Reference Include="UnityEngine.PhysicsModule"','<Reference Include="UnityEngine.AssetBundleModule" HintPath="$(GameManaged)\\UnityEngine.AssetBundleModule.dll" Private="false" />\n  <Reference Include="UnityEngine.PhysicsModule"');p.write_text(s)
