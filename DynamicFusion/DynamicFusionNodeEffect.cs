using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Threading.Tasks;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.UndoRedo;

namespace YMM4_shape_fusion_plugin.DynamicFusion
{
    /// <summary>
    /// 動的融合の「対象」エフェクト。
    /// このエフェクトをかけたアイテムの位置・半径をDynamicFusionNodeManagerに登録するだけで、
    /// 見た目(入力画像)自体は一切変更せずそのまま通す。
    ///
    /// 既知の制約(TODO):
    ///  - v1はカメラ・回転・3D配置を考慮しない。アイテムのDraw.X/Yをそのまま2D座標として使う
    ///  - 半径はアイテムの拡大率(Zoom)と連動しない。見た目のサイズを変えたら半径も手動で合わせる必要あり
    ///  - InterItemNode由来の設計のため、制御側のOutputが呼ばれるタイミング次第で1フレーム遅れることがある
    /// </summary>
    [VideoEffect("動的融合:対象", ["描画"], ["fusion", "node", "融合", "対象"])]
    public class DynamicFusionNodeEffect : VideoEffectBase
    {
        public override string Label => "動的融合:対象";

        [Display(GroupName = "設定", Name = "融合ID", Description = "同じIDを持つ「動的融合:制御」と結びつきます。")]
        [AnimationSlider("F0", "番", 0, 9)]
        public Animation NodeId { get; } = new Animation(0, 0, 99);

        [Display(GroupName = "設定", Name = "半径",
            Description = "このアイテムを円とみなした場合の半径(px)。アイテムの拡大率とは連動しないので見た目に合わせて手動調整してください。")]
        [AnimationSlider("F0", "px", 1, 1000)]
        public Animation Radius { get; } = new Animation(100, 1, 4000);

        // 毎フレームUpdateを呼ばせるためのダミー(InterItemNode由来。内部動作の詳細は未検証だが同じ設計を踏襲)
        private readonly DynamicFusionDummyAnimatable dummy = new();

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription desc) => [];
        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => new DynamicFusionNodeEffectProcessor(devices, this);
        protected override IEnumerable<IAnimatable> GetAnimatables() => [NodeId, Radius, dummy];
    }

    internal class DynamicFusionNodeEffectProcessor : IVideoEffectProcessor, IDisposable
    {
        readonly IGraphicsDevicesAndContext devices;
        readonly DynamicFusionNodeEffect item;
        readonly Guid providerId = Guid.NewGuid();
        ID2D1Image? input;

        bool isDirty;
        int cachedId;
        Vector2 cachedPosition;
        float cachedRadius;

        public DynamicFusionNodeEffectProcessor(IGraphicsDevicesAndContext devices, DynamicFusionNodeEffect item)
        {
            this.devices = devices;
            this.item = item;
        }

        public DrawDescription Update(EffectDescription desc)
        {
            var frame = desc.ItemPosition.Frame;
            var length = desc.ItemDuration.Frame;
            var fps = desc.FPS;

            cachedId = (int)item.NodeId.GetValue(frame, length, fps);
            cachedRadius = (float)item.Radius.GetValue(frame, length, fps);

            // TODO: v1では回転・カメラを考慮せず、アイテムの描画位置をそのまま2D座標として使う
            var draw = desc.DrawDescription.Draw;
            cachedPosition = new Vector2(draw.X, draw.Y);

            isDirty = true;

            return desc.DrawDescription;
        }

        // ★InterItemNodeと同じ手口：YMM4が画像を求めてきた最後のタイミングで登録処理を行う
        public ID2D1Image? Output
        {
            get
            {
                if (isDirty)
                {
                    DynamicFusionNodeManager.UpdateNode(cachedId, providerId, new DynamicFusionNodeData
                    {
                        Position = cachedPosition,
                        Radius = cachedRadius,
                    });
                    isDirty = false;
                }
                return input;
            }
        }

        public void SetInput(ID2D1Image? input) => this.input = input;
        public void ClearInput() => input = null;

        public void Dispose()
        {
            DynamicFusionNodeManager.ClearNode(cachedId, providerId);
        }
    }

    internal class DynamicFusionDummyAnimatable : IAnimatable
    {
        public bool IsNotAnimated => false;

#pragma warning disable CS0067
        public event EventHandler<UndoRedoEventArgs>? UndoRedoCommandCreated;
#pragma warning restore CS0067

        public void BeginEdit() { }
        public ValueTask EndEditAsync() => new();
        public object GetValue(int frame, int duration, int fps) => frame;
        public object[] GetValues() => [];
        public void SetAnimationParameters(int animationLength, int videoFPS) { }
        public void SetKeyFrames(KeyFrames? keyFrames) { }
    }
}
