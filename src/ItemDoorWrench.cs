using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace MoveDoors
{
    public class ItemDoorWrench : Item
    {
        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            if (!firstEvent) return;
            if (blockSel == null) { handling = EnumHandHandling.NotHandled; return; }

            var world = byEntity.World;
            var block = world.BlockAccessor.GetBlock(blockSel.Position);
            if (!BlockOffsetManager.IsMovable(block))
            {
                handling = EnumHandHandling.NotHandled;
                return;
            }

            bool reset = byEntity.Controls.Sneak;
            var face = blockSel.Face ?? BlockFacing.UP;

            if (world.Side == EnumAppSide.Client)
            {
                int step = MoveDoorsModSystem.GetClientStep();
                MoveDoorsModSystem.Offsets?.ClientSendInteract(blockSel.Position, face, reset, step);
                (world.Api as ICoreClientAPI)?.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
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
