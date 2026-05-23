using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace MoveDoors
{
    public class GuiDialogMoveStep : GuiDialog
    {
        public override string ToggleKeyCombinationCode => "movedoors:stepmode";

        private static readonly int[] Steps = { 1, 2, 4, 8 };

        public GuiDialogMoveStep(ICoreClientAPI capi) : base(capi)
        {
            Compose();
        }

        private void Compose()
        {
            const int cell = 72;
            const int gap = 10;
            const int padding = 14;
            int innerW = Steps.Length * cell + (Steps.Length - 1) * gap;
            int innerH = cell + 16;

            var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            var bgBounds = ElementBounds.Fixed(0, 0, innerW + padding * 2, innerH + padding * 2 + 30);
            bgBounds.BothSizing = ElementSizing.Fixed;

            var composer = capi.Gui.CreateCompo("movedoors-stepmode", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("movedoors:stepmode-title"), OnTitleBarClose)
                .BeginChildElements(bgBounds);

            for (int i = 0; i < Steps.Length; i++)
            {
                int s = Steps[i];
                double x = padding + i * (cell + gap);
                double y = padding + 30;
                var bounds = ElementBounds.Fixed(x, y, cell, cell);

                composer.AddDynamicCustomDraw(bounds,
                    (ctx, surface, b) => DrawTile(ctx, b, s, GetClientStepSafe() == s),
                    "tile-" + s);

                composer.AddSmallButton("", () => { OnPick(s); return true; },
                    bounds, EnumButtonStyle.None, "btn-" + s);
            }

            composer.EndChildElements();
            SingleComposer = composer.Compose();
        }

        private static int GetClientStepSafe() => MoveDoorsModSystem.GetClientStep();

        private void OnPick(int s)
        {
            MoveDoorsModSystem.SetClientStep(s);
            // Redraw all tiles so the selected highlight updates.
            foreach (var step in Steps)
            {
                var elem = SingleComposer.GetCustomDraw("tile-" + step);
                elem?.Redraw();
            }
        }

        private void DrawTile(Context ctx, ElementBounds bounds, int step, bool selected)
        {
            double w = bounds.InnerWidth;
            double h = bounds.InnerHeight;

            // Background.
            ctx.SetSourceRGBA(0.08, 0.08, 0.10, 0.95);
            RoundRect(ctx, 0, 0, w, h, 6);
            ctx.Fill();

            // Selected border (gold) or idle border (gray).
            if (selected) ctx.SetSourceRGBA(1.0, 0.78, 0.22, 1.0);
            else ctx.SetSourceRGBA(0.45, 0.45, 0.45, 1.0);
            ctx.LineWidth = selected ? 3.0 : 1.5;
            RoundRect(ctx, 1, 1, w - 2, h - 2, 6);
            ctx.Stroke();

            // Chisel-style voxel grid: N×N where N = 16 / step.
            int n = 16 / step;
            double gridSize = System.Math.Min(w, h) - 22;
            double gx = (w - gridSize) / 2;
            double gy = (h - gridSize) / 2 - 3;

            ctx.SetSourceRGBA(0.82, 0.82, 0.85, 0.85);
            ctx.LineWidth = 1.0;
            double cell = gridSize / n;
            for (int i = 0; i <= n; i++)
            {
                ctx.MoveTo(gx + i * cell, gy);
                ctx.LineTo(gx + i * cell, gy + gridSize);
                ctx.MoveTo(gx, gy + i * cell);
                ctx.LineTo(gx + gridSize, gy + i * cell);
            }
            ctx.Stroke();

            // 4-way arrow overlay.
            double cx = gx + gridSize / 2;
            double cy = gy + gridSize / 2;
            double r = gridSize * 0.34;

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

            // Label.
            ctx.SetSourceRGBA(1, 1, 1, 0.92);
            ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(13);
            string label = step + "/16";
            var ext = ctx.TextExtents(label);
            ctx.MoveTo((w - ext.Width) / 2, h - 6);
            ctx.ShowText(label);
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
