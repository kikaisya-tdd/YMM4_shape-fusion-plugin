using System;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Shape;

namespace YMM4_shape_fusion_plugin.CircleFusion
{
	/// <summary>
	/// 2つの円を接続割合yに応じてベジェ曲線で滑らかに繋いだ1つのジオメトリとして描画する。
	///
	/// 幾何学的な考え方:
	///  ・円1中心c1から円2中心c2への方向ベクトルをdir、その垂直方向をperpとする
	///  ・各円について、dir方向(=互いに最も近い点a)を基準角0、-dir方向(=互いに最も遠い点b)を基準角πとし、
	///    角度 θ = y×π の位置にある上下対称な2点を接続点として使う
	///  ・接続点同士(上側同士・下側同士)を3次ベジェで結び、残りは円周(遠い側の大きい弧)をそのまま使う
	///  ・ベジェの制御点は、各接続点における「円の接線方向」に沿って配置する(円弧との接線連続=G1連続を保つため)
	/// </summary>
	internal class ShapeFusionSource : IShapeSource
	{
		readonly IGraphicsDevicesAndContext devices;
		readonly ShapeFusionParameter parameter;
		readonly ID2D1SolidColorBrush whiteBrush;
		ID2D1CommandList? commandList;

		// 再計算要否判定用に前回値を保持
		float lastX1, lastY1, lastR1, lastX2, lastY2, lastR2, lastRatio, lastBound;
		bool isFirst = true;

		public ID2D1Image Output =>
			commandList ?? throw new InvalidOperationException("Update()を呼び出す前にOutputは参照できません。");

		public ShapeFusionSource(IGraphicsDevicesAndContext devices, ShapeFusionParameter parameter)
		{
			this.devices = devices;
			this.parameter = parameter;
			whiteBrush = devices.DeviceContext.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
		}

		public void Update(TimelineItemSourceDescription desc)
		{
			var fps = desc.FPS;
			var frame = desc.ItemPosition.Frame;
			var length = desc.ItemDuration.Frame;

			float x1 = (float)parameter.Circle1X.GetValue(frame, length, fps);
			float y1 = (float)parameter.Circle1Y.GetValue(frame, length, fps);
			float r1 = (float)parameter.Circle1Radius.GetValue(frame, length, fps);
			float x2 = (float)parameter.Circle2X.GetValue(frame, length, fps);
			float y2 = (float)parameter.Circle2Y.GetValue(frame, length, fps);
			float r2 = (float)parameter.Circle2Radius.GetValue(frame, length, fps);
			float ratio = (float)(parameter.ConnectRatio.GetValue(frame, length, fps) / 100.0);
			float bound = (float)(parameter.ConnectBound.GetValue(frame, length, fps) / 100.0);

			if (!isFirst
				&& x1 == lastX1 && y1 == lastY1 && r1 == lastR1
				&& x2 == lastX2 && y2 == lastY2 && r2 == lastR2
				&& ratio == lastRatio
				&& bound == lastBound)
			{
				return;
			}

			var dc = devices.DeviceContext;

			commandList?.Dispose();
			commandList = dc.CreateCommandList();

			dc.Target = commandList;
			dc.BeginDraw();
			dc.Clear(null);

			using (var geometry = BuildGeometry(new Vector2(x1, y1), r1, new Vector2(x2, y2), r2, ratio, bound))
			{
				if (geometry != null)
					dc.FillGeometry(geometry, whiteBrush);
			}

			dc.EndDraw();
			dc.Target = null;
			commandList.Close();

			isFirst = false;
			lastX1 = x1; lastY1 = y1; lastR1 = r1;
			lastX2 = x2; lastY2 = y2; lastR2 = r2;
			lastRatio = ratio;
			lastBound = bound;
		}

		ID2D1PathGeometry? BuildGeometry(Vector2 c1, float r1, Vector2 c2, float r2, float ratio, float bound)
		{
			if (r1 <= 0 && r2 <= 0)
				return null;

			var delta = c2 - c1;
			var dist = delta.Length();

			// 中心がほぼ重なっていて方向が定義できない場合は接続をあきらめ、大きい方の円だけ描画する
			if (dist < 0.0001f)
				return BuildSingleCircle(c1, MathF.Max(r1, r2));

			var dir = delta / dist;
			var perp = new Vector2(-dir.Y, dir.X);

			// ratio=0または1ちょうどだと接続点が縮退して不安定になるため僅かに内側にクランプ
			ratio = Math.Clamp(ratio, 0.001f, 0.999f);
			float theta = ratio * MathF.PI;
			float cosT = MathF.Cos(theta);
			float sinT = MathF.Sin(theta);

			// 円1側の接続点(基準方向 = dir、円2に最も近い方向)
			var p1Upper = c1 + r1 * (cosT * dir + sinT * perp);
			var p1Lower = c1 + r1 * (cosT * dir - sinT * perp);

			// 円2側の接続点(基準方向 = -dir、円1に最も近い方向)
			var p2Upper = c2 + r2 * (-cosT * dir + sinT * perp);
			var p2Lower = c2 + r2 * (-cosT * dir - sinT * perp);

			// TODO: Factoryの取得経路はIGraphicsDevicesAndContextの実メンバーに合わせて要確認
			var factory = devices.DeviceContext.Factory;
			var geometry = factory.CreatePathGeometry();

			using (var sink = geometry.Open())
			{
				sink.SetFillMode(FillMode.Winding);

				sink.BeginFigure(ToPoint(p1Upper), FigureBegin.Filled);

				// 円1の「遠回り」の弧(p1Upper → 最遠点b1 → p1Lower)
				// 弧の中心角 = 2π - 2θ。これがπを超えるかどうかでArcSizeを切り替える
				bool arcIsLarge = theta < MathF.PI / 2f;

				sink.AddArc(new ArcSegment
				{
					Point = ToPoint(p1Lower),
					Size = new Vortice.Mathematics.Size(r1, r1),
					RotationAngle = 0f,
					SweepDirection = SweepDirection.Clockwise,
					ArcSize = arcIsLarge ? ArcSize.Large : ArcSize.Small,
				});

				// 下側の接続ベジェ(p1Lower → p2Lower)
				// 制御点は各接続点の「円の接線方向」に沿わせることで、円弧との繋ぎ目を接線連続(G1連続)にする。
				// 固定のperp方向だけを使うと、θや膨らみ割合がマイナスの時にズレが目立って凹みの原因になっていた。
				var (ctrlA, ctrlB) = MakeConnectorControlPoints(p1Lower, p2Lower, c1, c2, r1, r2, bound);
				sink.AddBezier(new BezierSegment
				{
					Point1 = ToPoint(ctrlA),
					Point2 = ToPoint(ctrlB),
					Point3 = ToPoint(p2Lower),
				});

				// 円2の「遠回り」の弧(p2Lower → 最遠点b2 → p2Upper)
				sink.AddArc(new ArcSegment
				{
					Point = ToPoint(p2Upper),
					Size = new Vortice.Mathematics.Size(r2, r2),
					RotationAngle = 0f,
					SweepDirection = SweepDirection.Clockwise,
					ArcSize = arcIsLarge ? ArcSize.Large : ArcSize.Small,
				});

				// 上側の接続ベジェ(p2Upper → p1Upper)
				var (ctrlC, ctrlD) = MakeConnectorControlPoints(p2Upper, p1Upper, c2, c1, r2, r1, bound);
				sink.AddBezier(new BezierSegment
				{
					Point1 = ToPoint(ctrlC),
					Point2 = ToPoint(ctrlD),
					Point3 = ToPoint(p1Upper),
				});

				sink.EndFigure(FigureEnd.Closed);
				sink.Close();
			}

			return geometry;
		}

		/// <summary>
		/// 接続ベジェの制御点を作る。
		/// from/toそれぞれの接続点における円の接線方向(TangentTowards)に沿って、
		/// 半径×bulge の分だけ張り出させる。これにより円弧とベジェの接続部が接線連続になり、
		/// bulgeがマイナスでも(逆向きに張り出すだけなので)クセ・凹みのないなめらかな曲線になる。
		/// </summary>
		static (Vector2, Vector2) MakeConnectorControlPoints(Vector2 from, Vector2 to, Vector2 fromCenter, Vector2 toCenter, float rFrom, float rTo, float bulge)
		{
			var tangentFrom = TangentTowards(from, fromCenter, to - from);
			var tangentTo = TangentTowards(to, toCenter, from - to);

			var ctrl1 = from + tangentFrom * rFrom * bulge;
			var ctrl2 = to + tangentTo * rTo * bulge;
			return (ctrl1, ctrl2);
		}

		/// <summary>
		/// 点pにおける円(中心c)の接線方向を求める。
		/// 接線は半径ベクトルに垂直な2方向あるため、目安ベクトルhintとの内積が正になる側を選ぶ。
		/// TODO: 接続割合が0%/100%に極端に近い場合、hintと接線がほぼ直交してしまい
		/// 符号選択が不安定になる可能性がある(その付近は繋ぎ目自体がごく小さくなるため実害は少ない想定)。
		/// </summary>
		static Vector2 TangentTowards(Vector2 p, Vector2 c, Vector2 hint)
		{
			var radiusDir = Vector2.Normalize(p - c);
			var tangent = new Vector2(-radiusDir.Y, radiusDir.X);
			if (Vector2.Dot(tangent, hint) < 0)
				tangent = -tangent;
			return tangent;
		}

		ID2D1PathGeometry BuildSingleCircle(Vector2 center, float r)
		{
			var factory = devices.DeviceContext.Factory;
			var geometry = factory.CreatePathGeometry();
			using (var sink = geometry.Open())
			{
				sink.SetFillMode(FillMode.Winding);
				sink.BeginFigure(ToPoint(center + new Vector2(r, 0)), FigureBegin.Filled);
				sink.AddArc(new ArcSegment
				{
					Point = ToPoint(center - new Vector2(r, 0)),
					Size = new Vortice.Mathematics.Size(r, r),
					RotationAngle = 0f,
					SweepDirection = SweepDirection.CounterClockwise,
					ArcSize = ArcSize.Small,
				});
				sink.AddArc(new ArcSegment
				{
					Point = ToPoint(center + new Vector2(r, 0)),
					Size = new Vortice.Mathematics.Size(r, r),
					RotationAngle = 0f,
					SweepDirection = SweepDirection.CounterClockwise,
					ArcSize = ArcSize.Small,
				});
				sink.EndFigure(FigureEnd.Closed);
				sink.Close();
			}
			return geometry;
		}

		// TODO: Vortice.Mathematics側のVector2/Point型がSystem.Numerics.Vector2とそのまま
		// 互換になっているかはインストール済みバージョンに依存するため要確認。
		// 互換でない場合はここで明示的に変換してください。
		static Vector2 ToPoint(Vector2 v) => v;

		public void Dispose()
		{
			commandList?.Dispose();
			whiteBrush.Dispose();
		}
	}
}