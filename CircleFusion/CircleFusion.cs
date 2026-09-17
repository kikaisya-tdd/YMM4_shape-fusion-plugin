using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace SFP.CircleFusion;
public class ShapeFusionPlugin : IShapePlugin
{
	public string Name => "図形融合プラグイン:円";
	public bool IsExoShapeSupported => false;
	public bool IsExoMaskSupported => false;
	public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
			=> new ShapeFusionParameter(sharedData);
}
