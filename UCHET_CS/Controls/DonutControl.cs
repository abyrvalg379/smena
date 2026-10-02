using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UCHET.Controls
{
    public class DonutItem
    {
        public double Value { get; set; }
        public Brush Brush { get; set; } = Brushes.Gray;
    }

    /// <summary>Segmented donut, custom OnRender — no chart libraries.</summary>
    public class DonutControl : FrameworkElement
    {
        public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
            nameof(Items), typeof(List<DonutItem>), typeof(DonutControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public List<DonutItem> Items
        {
            get => (List<DonutItem>)GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        public static readonly DependencyProperty CenterTitleProperty = DependencyProperty.Register(
            nameof(CenterTitle), typeof(string), typeof(DonutControl),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public string CenterTitle
        {
            get => (string)GetValue(CenterTitleProperty);
            set => SetValue(CenterTitleProperty, value);
        }

        public static readonly DependencyProperty CenterValueProperty = DependencyProperty.Register(
            nameof(CenterValue), typeof(string), typeof(DonutControl),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public string CenterValue
        {
            get => (string)GetValue(CenterValueProperty);
            set => SetValue(CenterValueProperty, value);
        }

        private static Color ThemeColor(string key, Color fallback)
        {
            if (Services.ThemeApplier.OptBrush(key) is SolidColorBrush b) return b.Color;
            return fallback;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth; var h = ActualHeight;
            if (w < 10 || h < 10) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

            var cx = w / 2; var cy = h / 2;
            double radius = Math.Min(w, h) / 2 - 12;
            double thickness = 16;

            var track = new Pen(new SolidColorBrush(
                ThemeColor("Track", Color.FromRgb(0x1B, 0x24, 0x30))), thickness);
            track.Freeze();
            dc.DrawEllipse(null, track, new Point(cx, cy), radius, radius);

            var items = Items;
            double total = 0;
            if (items != null) foreach (var it in items) total += Math.Max(0, it.Value);

            if (total > 0 && items != null)
            {
                double angle = -90;
                foreach (var it in items)
                {
                    var v = Math.Max(0, it.Value);
                    if (v <= 0) continue;
                    var sweep = v / total * 360.0;
                    if (sweep >= 360) sweep = 359.99;
                    var pen = new Pen(it.Brush, thickness);
                    pen.StartLineCap = PenLineCap.Flat;
                    pen.EndLineCap = PenLineCap.Flat;
                    var a1 = angle * Math.PI / 180;
                    var a2 = (angle + sweep) * Math.PI / 180;
                    var p1 = new Point(cx + radius * Math.Cos(a1), cy + radius * Math.Sin(a1));
                    var p2 = new Point(cx + radius * Math.Cos(a2), cy + radius * Math.Sin(a2));
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(p1, false, false);
                        ctx.ArcTo(p2, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
                    }
                    dc.DrawGeometry(null, pen, geo);
                    angle += sweep;
                }
            }

            var tfTitle = new FormattedText(CenterTitle ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, new SolidColorBrush(
                    ThemeColor("FgDim", Color.FromRgb(0x89, 0x95, 0xA5))),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var tfValue = new FormattedText(CenterValue ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 16, new SolidColorBrush(
                    ThemeColor("Fg", Color.FromRgb(0xE8, 0xED, 0xF3))),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(tfTitle, new Point(cx - tfTitle.Width / 2, cy - tfValue.Height));
            dc.DrawText(tfValue, new Point(cx - tfValue.Width / 2, cy - tfValue.Height / 2 + 6));
        }
    }
}
