using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace MoveDoors
{
    public class GuiDialogMoveStep : GuiDialog
    {
        public override string ToggleKeyCombinationCode => "movedoors:stepmode";

        private static readonly WrenchMode[] Modes = {
            WrenchMode.Rotate, WrenchMode.Move1, WrenchMode.Move2, WrenchMode.Move4
        };

        public GuiDialogMoveStep(ICoreClientAPI capi) : base(capi)
        {
            Compose();
        }

        private void Compose()
        {
            const int cell = 72;
            const int gap = 10;
            const int padding = 14;
            int innerW = Modes.Length * cell + (Modes.Length - 1) * gap;
            int innerH = cell + 16;

            var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            var bgBounds = ElementBounds.Fixed(0, 0, innerW + padding * 2, innerH + padding * 2 + 30);
            bgBounds.BothSizing = ElementSizing.Fixed;

            var composer = capi.Gui.CreateCompo("movedoors-stepmode", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("movedoors:stepmode-title"), OnTitleBarClose)
                .BeginChildElements(bgBounds);

            for (int i = 0; i < Modes.Length; i++)
            {
                var mode = Modes[i];
                double x = padding + i * (cell + gap);
                double y = padding + 30;
                var bounds = ElementBounds.Fixed(x, y, cell, cell);

                composer.AddDynamicCustomDraw(bounds,
                    (ctx, surface, b) => DrawTile(ctx, b, mode, MoveDoorsModSystem.GetClientMode() == mode),
                    "tile-" + (int)mode);

                composer.AddSmallButton("", () => { OnPick(mode); return true; },
                    bounds, EnumButtonStyle.None, "btn-" + (int)mode);
            }

            composer.EndChildElements();
            SingleComposer = composer.Compose();
        }

        private void OnPick(WrenchMode m)
        {
            MoveDoorsModSystem.SetClientMode(m);
            foreach (var mode in Modes)
            {
                SingleComposer.GetCustomDraw("tile-" + (int)mode)?.Redraw();
            }
        }

        private void DrawTile(Context ctx, ElementBounds bounds, WrenchMode mode, bool selected)
        {
            double w = bounds.InnerWidth;
            double h = bounds.InnerHeight;

            // Background.
            ctx.SetSourceRGBA(0.08, 0.08, 0.10, 0.95);
            RoundRect(ctx, 0, 0, w, h, 6);
            ctx.Fill();

            // Border (gold if selected).
            if (selected) ctx.SetSourceRGBA(1.0, 0.78, 0.22, 1.0);
            else ctx.SetSourceRGBA(0.45, 0.45, 0.45, 1.0);
            ctx.LineWidth = selected ? 3.0 : 1.5;
            RoundRect(ctx, 1, 1, w - 2, h - 2, 6);
            ctx.Stroke();

            // Icon area (room for label below).
            double iconSize = Math.Min(w, h) - 22;
            double ix = (w - iconSize) / 2;
            double iy = (h - iconSize) / 2 - 3;

            if (mode == WrenchMode.Rotate)
            {
                DrawRotateIcon(ctx, ix, iy, iconSize);
            }
            else
            {
                int n = mode switch { WrenchMode.Move1 => 16, WrenchMode.Move2 => 8, WrenchMode.Move4 => 4, _ => 16 };
                DrawMoveIcon(ctx, ix, iy, iconSize, n);
            }

            // Label.
            ctx.SetSourceRGBA(1, 1, 1, 0.92);
            ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(13);
            string label = mode switch {
                WrenchMode.Rotate => "Rotate",
                WrenchMode.Move1 => "1/16",
                WrenchMode.Move2 => "2/16",
                WrenchMode.Move4 => "4/16",
                _ => "?"
            };
            var ext = ctx.TextExtents(label);
            ctx.MoveTo((w - ext.Width) / 2, h - 6);
            ctx.ShowText(label);
        }

        private static void DrawMoveIcon(Context ctx, double ox, double oy, double size, int n)
        {
            // Voxel grid (n×n) — chisel-style.
            ctx.SetSourceRGBA(0.82, 0.82, 0.85, 0.85);
            ctx.LineWidth = 1.0;
            double cell = size / n;
            for (int i = 0; i <= n; i++)
            {
                ctx.MoveTo(ox + i * cell, oy);
                ctx.LineTo(ox + i * cell, oy + size);
                ctx.MoveTo(ox, oy + i * cell);
                ctx.LineTo(ox + size, oy + i * cell);
            }
            ctx.Stroke();

            // 4-way arrow overlay.
            double cx = ox + size / 2;
            double cy = oy + size / 2;
            double r = size * 0.34;

            ctx.SetSourceRGBA(1.0, 0.85, 0.30, 0.95);
            ctx.LineWidth = 2.4;
            ctx.MoveTo(cx - r, cy); ctx.LineTo(cx + r, cy);
            ctx.MoveTo(cx, cy - r); ctx.LineTo(cx, cy + r);
            ctx.Stroke();

            double a = r * 0.30;
            ctx.MoveTo(cx + r, cy); ctx.LineTo(cx + r - a, cy - a); ctx.LineTo(cx + r - a, cy + a); ctx.ClosePath();
            ctx.MoveTo(cx - r, cy); ctx.LineTo(cx - r + a, cy - a); ctx.LineTo(cx - r + a, cy + a); ctx.ClosePath();
            ctx.MoveTo(cx, cy - r); ctx.LineTo(cx - a, cy - r + a); ctx.LineTo(cx + a, cy - r + a); ctx.ClosePath();
            ctx.MoveTo(cx, cy + r); ctx.LineTo(cx - a, cy + r - a); ctx.LineTo(cx + a, cy + r - a); ctx.ClosePath();
            ctx.Fill();
        }

        private static void DrawRotateIcon(Context ctx, double ox, double oy, double size)
        {
            double cx = ox + size / 2;
            double cy = oy + size / 2;
            double r = size * 0.34;

            ctx.SetSourceRGBA(1.0, 0.85, 0.30, 0.95);
            ctx.LineWidth = 3.0;

            // Circular arrow (3/4 arc).
            ctx.Arc(cx, cy, r, Math.PI * 0.15, Math.PI * 1.5);
            ctx.Stroke();

            // Arrowhead at the end of the arc.
            double endAngle = Math.PI * 1.5;
            double ex = cx + r * Math.Cos(endAngle);
            double ey = cy + r * Math.Sin(endAngle);
            double a = r * 0.45;

            ctx.MoveTo(ex, ey);
            ctx.LineTo(ex - a, ey - a * 0.55);
            ctx.LineTo(ex - a * 0.5, ey + a);
            ctx.ClosePath();
            ctx.Fill();
        }

        private static void RoundRect(Context ctx, double x, double y, double w, double h, double r)
        {
            ctx.MoveTo(x + r, y);
            ctx.LineTo(x + w - r, y);
            ctx.CurveTo(x + w, y, x + w, y, x + w, y + r);
            ctx.LineTo(x + w, y + h - r);
            ctx.CurveTo(x + w, y + h, x + w, y + h, x + w - r, y + h);
            ctx.LineTo(x + r, y + h);
            ctx.CurveTo(x, y + h, x, y + h, x, y + h - r);
            ctx.LineTo(x, y + r);
            ctx.CurveTo(x, y, x, y, x + r, y);
        }

        private void OnTitleBarClose() => TryClose();

        public override bool PrefersUngrabbedMouse => true;
    }
}
