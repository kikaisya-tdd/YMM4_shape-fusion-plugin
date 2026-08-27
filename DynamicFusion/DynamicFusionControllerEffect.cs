using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace YMM4_shape_fusion_plugin.DynamicFusion
{
    /// <summary>
    /// 動的融合の「制御」エフェクト。
    /// 2つの融合IDに登録された円(位置・半径)をDynamicFusionNodeManagerから読み取り、
    /// 図形融合(ShapeFusion)と同じベジェ接続アルゴリズムで合体形状を描画する。
    /// サイズ0の専用アイテムなどに付けて使うことを想定。
    ///
    /// 既知の制約(TODO):
    ///  - 描画順序(Outputが呼ばれるタイミング)に依存するため、1フレーム遅れることがある
    ///  - v1はカメラ・回転を考慮しない2D固定
    /// </summary>
    [VideoEffect("動的融合:制御", ["描画"], ["fusion", "controller", "融合", "制御"])]
    public class DynamicFusionControllerEffect : VideoEffectBase
    {
        public override string Label => "動的融合:制御";

        [Display(GroupName = "接続", Name = "対象AのID")]
        [AnimationSlider("F0", "番", 0, 9)]
        public Animation NodeIdA { get; } = new Animation(0, 0, 99);

        [Display(GroupName = "接続", Name = "対象BのID")]
        [AnimationSlider("F0", "番", 0, 9)]
        public Animation NodeIdB { get; } = new Animation(1, 0, 99);

        [Display(GroupName = "接続", Name = "接続割合",
            Description = "0%で最近接点付近だけの細い接続、100%で最遠点付近まで到達しほぼ完全に一体化した見た目になります")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation ConnectRatio { get; } = new Animation(50, 0, 100);

        [Display(GroupName = "接続", Name = "膨らみ割合")]
        [AnimationSlider("F0", "%", -99, 99)]
        public Animation ConnectBound { get; } = new Animation(50, -99, 99);

        private readonly DynamicFusionDummyAnimatable dummy = new();

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription desc) => [];
        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => new DynamicFusionControllerEffectProcessor(devices, this);
        protected override IEnumerable<IAnimatable> GetAnimatables() => [NodeIdA, NodeIdB, ConnectRatio, ConnectBound, dummy];
    }

    internal class DynamicFusionControllerEffectProcessor : IVideoEffectProcessor, IDisposable
    {
        readonly IGraphicsDevicesAndContext devices;
        readonly DynamicFusionControllerEffect item;
        readonly ID2D1SolidColorBrush whiteBrush;
        ID2D1Image? input;
        ID2D1CommandList? outputCommandList;

        bool isDirty;
        int cachedIdA, cachedIdB;
        float cachedRatio, cachedBound;

        public DynamicFusionControllerEffectProcessor(IGraphicsDevicesAndContext devices, DynamicFusionControllerEffect item)
        {
            this.devices = devices;
            this.item = item;
            whiteBrush = devices.DeviceContext.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
        }

        public DrawDescription Update(EffectDescription desc)
        {
            var frame = desc.ItemPosition.Frame;
            var length = desc.ItemDuration.Frame;
            var fps = desc.FPS;

            cachedIdA = (int)item.NodeIdA.GetValue(frame, length, fps);
            cachedIdB = (int)item.NodeIdB.GetValue(frame, length, fps);
            cachedRatio = (float)(item.ConnectRatio.GetValue(frame, length, fps) / 100.0);
            cachedBound = (float)(item.ConnectBound.GetValue(frame, length, fps) / 100.0);

            isDirty = true;

            return desc.DrawDescription;
        }

        // ★対象側と同じ手口：YMM4が画像を求めてきた最後のタイミングで実際の描画を行う
        public ID2D1Image? Output
        {
            get
            {
                if (isDirty)
                {
                    GenerateCommandList();
                    isDirty = false;
                }
                return outputCommandList ?? input;
            }
        }

        void GenerateCommandList()
        {
            var dc = devices.DeviceContext;
            outputCommandList?.Dispose();
            outputCommandList = dc.CreateCommandList();

            var oldTarget = dc.Target;
            dc.Target = outputCommandList;
            dc.BeginDraw();
            dc.Clear(null);

            bool hasA = DynamicFusionNodeManager.TryGetFirstNode(cachedIdA, out var nodeA);
            bool hasB = DynamicFusionNodeManager.TryGetFirstNode(cachedIdB, out var nodeB);

            // 両方登録されているときだけ合体形状を描く(対象側がまだ1フレームも描画されていない場合などは何もしない)
            if (hasA && hasB)
            {
                using var geometry = BuildGeometry(nodeA.Position, nodeA.Radius, nodeB.Position, nodeB.Radius, cachedRatio, cachedBound);
                if (geometry != null)
                    dc.FillGeometry(geometry, whiteBrush);
            }

            if (input != null)
                dc.DrawImage(input);

            dc.EndDraw();
            dc.Target = oldTarget;
            outputCommandList.Close();
        }

        // --- 以下、ShapeFusionSourceと同じベジェ接続アルゴリズム(キャンバス空間の座標を直接扱う点のみ異なる) ---

        ID2D1PathGeometry? BuildGeometry(Vector2 c1, float r1, Vector2 c2, float r2, float ratio, float bound)
        {
            if (r1 <= 0 && r2 <= 0)
                return null;

            var delta = c2 - c1;
            var dist = delta.Length();
            if (dist < 0.0001f)
                return BuildSingleCircle(c1, MathF.Max(r1, r2));

            var dir = delta / dist;
            var perp = new Vector2(-dir.Y, dir.X);

            ratio = Math.Clamp(ratio, 0.001f, 0.999f);
            float theta = ratio * MathF.PI;
            float cosT = MathF.Cos(theta);
            float sinT = MathF.Sin(theta);

            var p1Upper = c1 + r1 * (cosT * dir + sinT * perp);
            var p1Lower = c1 + r1 * (cosT * dir - sinT * perp);
            var p2Upper = c2 + r2 * (-cosT * dir + sinT * perp);
            var p2Lower = c2 + r2 * (-cosT * dir - sinT * perp);

            var factory = devices.DeviceContext.Factory;
            var geometry = factory.CreatePathGeometry();

            using (var sink = geometry.Open())
            {
                sink.SetFillMode(FillMode.Winding);
                sink.BeginFigure(ToPoint(p1Upper), FigureBegin.Filled);

                bool arcIsLarge = theta < MathF.PI / 2f;

                sink.AddArc(new ArcSegment
                {
                    Point = ToPoint(p1Lower),
                    Size = new Vortice.Mathematics.Size(r1, r1),
                    RotationAngle = 0f,
                    SweepDirection = SweepDirection.Clockwise,
                    ArcSize = arcIsLarge ? ArcSize.Large : ArcSize.Small,
                });

                var (ctrlA, ctrlB) = MakeConnectorControlPoints(p1Lower, p2Lower, c1, c2, r1, r2, bound);
                sink.AddBezier(new BezierSegment { Point1 = ToPoint(ctrlA), Point2 = ToPoint(ctrlB), Point3 = ToPoint(p2Lower) });

                sink.AddArc(new ArcSegment
                {
                    Point = ToPoint(p2Upper),
                    Size = new Vortice.Mathematics.Size(r2, r2),
                    RotationAngle = 0f,
                    SweepDirection = SweepDirection.Clockwise,
                    ArcSize = arcIsLarge ? ArcSize.Large : ArcSize.Small,
                });

                var (ctrlC, ctrlD) = MakeConnectorControlPoints(p2Upper, p1Upper, c2, c1, r2, r1, bound);
                sink.AddBezier(new BezierSegment { Point1 = ToPoint(ctrlC), Point2 = ToPoint(ctrlD), Point3 = ToPoint(p1Upper) });

                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }

            return geometry;
        }

        static (Vector2, Vector2) MakeConnectorControlPoints(Vector2 from, Vector2 to, Vector2 fromCenter, Vector2 toCenter, float rFrom, float rTo, float bulge)
        {
            var tangentFrom = TangentTowards(from, fromCenter, to - from);
            var tangentTo = TangentTowards(to, toCenter, from - to);
            var ctrl1 = from + tangentFrom * rFrom * bulge;
            var ctrl2 = to + tangentTo * rTo * bulge;
            return (ctrl1, ctrl2);
        }

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
                sink.AddArc(new ArcSegment { Point = ToPoint(center - new Vector2(r, 0)), Size = new Vortice.Mathematics.Size(r, r), RotationAngle = 0f, SweepDirection = SweepDirection.CounterClockwise, ArcSize = ArcSize.Small });
                sink.AddArc(new ArcSegment { Point = ToPoint(center + new Vector2(r, 0)), Size = new Vortice.Mathematics.Size(r, r), RotationAngle = 0f, SweepDirection = SweepDirection.CounterClockwise, ArcSize = ArcSize.Small });
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }
            return geometry;
        }

        static Vector2 ToPoint(Vector2 v) => v;

        public void SetInput(ID2D1Image? input) => this.input = input;
        public void ClearInput() => input = null;

        public void Dispose()
        {
            outputCommandList?.Dispose();
            whiteBrush.Dispose();
        }
    }
}
