using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace GlobalTranslator
{
    internal sealed class ScreenshotSelector : Form
    {
        private readonly Bitmap _screen;
        private Point _start;
        private Point _current;
        private Rectangle _selection;
        private bool _dragging;
        private bool _rightCancelPending;

        public Bitmap SelectedBitmap { get; private set; }

        public ScreenshotSelector()
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            _screen = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(_screen))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);

            AutoScaleMode = AutoScaleMode.None;
            Text = "鲨译 OCR";
            Bounds = bounds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(_screen, 0, 0);
            using (var shade = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
                e.Graphics.FillRectangle(shade, ClientRectangle);

            if (!_selection.IsEmpty)
            {
                e.Graphics.DrawImage(_screen, _selection, _selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.FromArgb(39, 195, 214), 2))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, _selection);
                }
                string size = _selection.Width + " × " + _selection.Height;
                using (var font = new Font("Segoe UI", 10))
                using (var brush = new SolidBrush(Color.White))
                    e.Graphics.DrawString(
                        size, font, brush, _selection.Left, Math.Max(2, _selection.Top - 24));
            }
            if (_dragging) DrawMagnifier(e.Graphics);

            using (var font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold))
            using (var background = new SolidBrush(Color.FromArgb(220, 8, 42, 67)))
            using (var brush = new SolidBrush(Color.White))
            {
                const string hint = "鲨译 OCR  ·  拖动框选文字  ·  右键 / Esc 取消";
                SizeF size = e.Graphics.MeasureString(hint, font);
                var box = new RectangleF(
                    (ClientSize.Width - size.Width) / 2 - 18, 22,
                    size.Width + 36, size.Height + 16);
                e.Graphics.FillRectangle(background, box);
                e.Graphics.DrawString(hint, font, brush, box.Left + 18, box.Top + 8);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // Keep the overlay alive until button-up, otherwise the browser
                // underneath receives the release and opens its context menu.
                _rightCancelPending = true;
                _dragging = false;
                _selection = Rectangle.Empty;
                Capture = true;
                Invalidate();
                return;
            }
            if (_rightCancelPending) return;
            if (e.Button != MouseButtons.Left) return;
            _start = e.Location;
            _current = e.Location;
            _selection = Rectangle.Empty;
            _dragging = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            _current = e.Location;
            _selection = Normalize(_start, e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_rightCancelPending)
            {
                if (e.Button == MouseButtons.Right)
                {
                    _rightCancelPending = false;
                    CancelSelection();
                }
                return;
            }
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;
            Capture = false;
            _selection = Rectangle.Intersect(Normalize(_start, e.Location), ClientRectangle);
            if (_selection.Width < 8 || _selection.Height < 8)
            {
                _selection = Rectangle.Empty;
                Invalidate();
                return;
            }
            Rectangle captureArea = _selection;
            captureArea.Inflate(6, 6);
            captureArea = Rectangle.Intersect(captureArea, ClientRectangle);
            SelectedBitmap = _screen.Clone(captureArea, PixelFormat.Format32bppArgb);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (!_rightCancelPending) CancelSelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void CancelSelection()
        {
            _dragging = false;
            Capture = false;
            _selection = Rectangle.Empty;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _screen.Dispose();
            base.Dispose(disposing);
        }

        private static Rectangle Normalize(Point first, Point second)
        {
            return Rectangle.FromLTRB(
                Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
        }

        private void DrawMagnifier(Graphics graphics)
        {
            const int sampleSize = 32;
            const int magnifierSize = 128;
            int sourceX = Math.Max(
                0, Math.Min(_screen.Width - sampleSize, _current.X - sampleSize / 2));
            int sourceY = Math.Max(
                0, Math.Min(_screen.Height - sampleSize, _current.Y - sampleSize / 2));
            int left = _current.X + 24;
            int top = _current.Y + 24;
            if (left + magnifierSize + 8 > ClientSize.Width)
                left = _current.X - magnifierSize - 24;
            if (top + magnifierSize + 8 > ClientSize.Height)
                top = _current.Y - magnifierSize - 24;
            left = Math.Max(8, left);
            top = Math.Max(8, top);
            var destination = new Rectangle(
                left, top, magnifierSize, magnifierSize);
            using (var shadow = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                graphics.FillRectangle(
                    shadow, destination.Left + 4, destination.Top + 4,
                    destination.Width, destination.Height);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(
                _screen, destination,
                new Rectangle(sourceX, sourceY, sampleSize, sampleSize),
                GraphicsUnit.Pixel);
            using (var border = new Pen(Color.FromArgb(39, 195, 214), 2))
                graphics.DrawRectangle(border, destination);
            using (var cross = new Pen(Color.FromArgb(220, 8, 42, 67), 1))
            {
                int centerX = destination.Left + destination.Width / 2;
                int centerY = destination.Top + destination.Height / 2;
                graphics.DrawLine(
                    cross, centerX, destination.Top, centerX, destination.Bottom);
                graphics.DrawLine(
                    cross, destination.Left, centerY, destination.Right, centerY);
            }
        }
    }
}
