using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace MoveDoors
{
    public class ItemDoorWrench : Item
    {
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine();
            dsc.AppendLine("Build " + BuildInfo.Version + " " + BuildInfo.Sha);
            dsc.AppendLine(BuildInfo.Stamp);
        }

        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            var w = byEntity.World;
            w.Logger.Notification("[movedoors] OnHeldInteractStart side=" + w.Side
                + " first=" + firstEvent
                + " blockSel=" + (blockSel?.Position?.ToString() ?? "null"));

            if (!firstEvent) return;
            if (blockSel == null) { handling = EnumHandHandling.NotHandled; return; }

            var world = byEntity.World;
            var block = world.BlockAccessor.GetBlock(blockSel.Position);
            world.Logger.Notification("[movedoors] target block: " + block?.Code + " class=" + block?.GetType().Name);

            if (!BlockOffsetManager.IsMovable(block))
            {
                world.Logger.Notification("[movedoors] block not movable; bailing");
                handling = EnumHandHandling.NotHandled;
                return;
            }

            bool reset = byEntity.Controls.Sneak;
            var face = blockSel.Face ?? BlockFacing.UP;

            if (world.Side == EnumAppSide.Client)
            {
                int step = MoveDoorsModSystem.GetClientStep();
                var capi = world.Api as ICoreClientAPI;
                capi?.ShowChatMessage("[movedoors] wrench fired on " + block.Code + " step=" + step + (reset ? " RESET" : ""));
                MoveDoorsModSystem.Offsets?.ClientSendInteract(blockSel.Position, face, reset, step);
                capi?.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            }

            handling = EnumHandHandling.PreventDefault;
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "movedoors:heldhelp-shift",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction
                {
                    ActionLangCode = "movedoors:heldhelp-reset",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak"
                },
                new WorldInteraction
                {
                    ActionLangCode = "movedoors:heldhelp-step",
                    HotKeyCode = "movedoors:stepmode"
                }
            };
        }
    }
}
