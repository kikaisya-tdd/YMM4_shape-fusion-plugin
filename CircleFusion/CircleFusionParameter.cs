using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace YMM4_shape_fusion_plugin.CircleFusion
{
	/// <summary>
	/// 円1・円2の位置/半径と、接続割合(y)を保持するパラメータークラス。
	/// すべてAnimation型なのでキーフレームで動かせる(円を近づけていくと自動で繋がって見える演出が可能)。
	/// </summary>
	internal class ShapeFusionParameter(SharedDataStore? sharedData) : ShapeParameterBase(sharedData)
	{
		[Display(GroupName = "円1", Name = "X", Description = "円1の中心X座標")]
		[AnimationSlider("F0", "px", -4000, 4000)]
		public Animation Circle1X { get; } = new Animation(-150, -8000, 8000);

		[Display(GroupName = "円1", Name = "Y", Description = "円1の中心Y座標")]
		[AnimationSlider("F0", "px", -4000, 4000)]
		public Animation Circle1Y { get; } = new Animation(0, -8000, 8000);

		[Display(GroupName = "円1", Name = "半径", Description = "円1の半径")]
		[AnimationSlider("F0", "px", 1, 1000)]
		public Animation Circle1Radius { get; } = new Animation(100, 1, 4000);

		[Display(GroupName = "円2", Name = "X", Description = "円2の中心X座標")]
		[AnimationSlider("F0", "px", -4000, 4000)]
		public Animation Circle2X { get; } = new Animation(150, -8000, 8000);

		[Display(GroupName = "円2", Name = "Y", Description = "円2の中心Y座標")]
		[AnimationSlider("F0", "px", -4000, 4000)]
		public Animation Circle2Y { get; } = new Animation(0, -8000, 8000);

		[Display(GroupName = "円2", Name = "半径", Description = "円2の半径")]
		[AnimationSlider("F0", "px", 1, 1000)]
		public Animation Circle2Radius { get; } = new Animation(100, 1, 4000);

		[Display(GroupName = "接続", Name = "接続割合",
			Description = "0%で最近接点付近だけの細い接続、100%で最遠点付近まで到達しほぼ完全に一体化した見た目になります")]
		[AnimationSlider("F0", "%", 0, 100)]
		public Animation ConnectRatio { get; } = new Animation(30, 0, 100);
	
		[Display(GroupName = "接続", Name = "膨らみ割合",
			Description = "0%で最近接点付近だけの細い接続、100%で最遠点付近まで到達しほぼ完全に一体化した見た目になります")]
		[AnimationSlider("F0", "%", -120, 120)]
		public Animation ConnectBound { get; } = new Animation(50, -120, 120);
		//必ず引数なしのコンストラクタを定義する。
		//これがないとプロジェクトファイルの読み込みに失敗する(サンプルコード準拠)。
		public ShapeFusionParameter() : this(null)
		{
		}

		public override IShapeSource CreateShapeSource(IGraphicsDevicesAndContext devices)
			=> new ShapeFusionSource(devices, this);

		public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc)
			=> [];

		public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskParameters)
			=> [];

		protected override IEnumerable<IAnimatable> GetAnimatables()
			=> [Circle1X, Circle1Y, Circle1Radius, Circle2X, Circle2Y, Circle2Radius, ConnectRatio, ConnectBound];

		/// <summary>
		/// 図形の種類を切り替えたときに元の設定項目を復元するための一時保存処理(サンプルコード準拠)。
		/// </summary>
		protected override void LoadSharedData(SharedDataStore store)
		{
			var sharedData = store.Load<SharedData>();
			if (sharedData is null)
				return;

			sharedData.CopyTo(this);
		}

		protected override void SaveSharedData(SharedDataStore store)
		{
			store.Save(new SharedData(this));
		}

		/// <summary>
		/// 設定の一時保存用クラス
		/// </summary>
		class SharedData
		{
			public Animation Circle1X { get; } = new Animation(-150, -8000, 8000);
			public Animation Circle1Y { get; } = new Animation(0, -8000, 8000);
			public Animation Circle1Radius { get; } = new Animation(100, 1, 4000);
			public Animation Circle2X { get; } = new Animation(150, -8000, 8000);
			public Animation Circle2Y { get; } = new Animation(0, -8000, 8000);
			public Animation Circle2Radius { get; } = new Animation(100, 1, 4000);
			public Animation ConnectRatio { get; } = new Animation(30, 0, 100);
			public Animation ConnectBound { get; } = new Animation(50, -120, 120);

			public SharedData(ShapeFusionParameter param)
			{
				Circle1X.CopyFrom(param.Circle1X);
				Circle1Y.CopyFrom(param.Circle1Y);
				Circle1Radius.CopyFrom(param.Circle1Radius);
				Circle2X.CopyFrom(param.Circle2X);
				Circle2Y.CopyFrom(param.Circle2Y);
				Circle2Radius.CopyFrom(param.Circle2Radius);
				ConnectRatio.CopyFrom(param.ConnectRatio);
				ConnectBound.CopyFrom(param.ConnectBound);
			}

			public void CopyTo(ShapeFusionParameter param)
			{
				param.Circle1X.CopyFrom(Circle1X);
				param.Circle1Y.CopyFrom(Circle1Y);
				param.Circle1Radius.CopyFrom(Circle1Radius);
				param.Circle2X.CopyFrom(Circle2X);
				param.Circle2Y.CopyFrom(Circle2Y);
				param.Circle2Radius.CopyFrom(Circle2Radius);
				param.ConnectRatio.CopyFrom(ConnectRatio);
				param.ConnectBound.CopyFrom(ConnectBound);
			}
		}
	}
}
