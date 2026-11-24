using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Notka.Desktop.Taskbar
{
    internal sealed unsafe class StripRenderer : IDisposable
    {
        private const string FontFace = "Segoe UI Variable Text";

        private HDC _dc;
        private HBITMAP _bitmap;
        private HGDIOBJ _previousBitmap;
        private IntPtr _font;
        private HGDIOBJ _previousFont;
        private double _fontScale;
        private uint* _pixels;
        private int _width;
        private int _height;

        public StripRenderer()
        {
            var screen = PInvoke.GetDC(HWND.Null);
            _dc = PInvoke.CreateCompatibleDC(screen);
            PInvoke.ReleaseDC(HWND.Null, screen);
        }

        public IntPtr DcHandle => (IntPtr)_dc.Value;

        public int MeasureTextWidth(string text, double scale)
        {
            EnsureFont(scale);
            var bounds = new RECT();
            fixed (char* chars = text)
            {
                PInvoke.DrawText(_dc, chars, text.Length, ref bounds, DRAW_TEXT_FORMAT.DT_CALCRECT | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            }

            return bounds.right - bounds.left;
        }

        public void Render(string text, bool light, bool hover, int width, int height, double scale)
        {
            EnsureBitmap(width, height);
            EnsureFont(scale);

            var background = light
                ? (hover ? 0xFFD6D6D6u : 0xFFE8E8E8u)
                : (hover ? 0xFF4A4A4Au : 0xFF3A3A3Au);
            for (var i = 0; i < width * height; i++)
            {
                _pixels[i] = background;
            }

            PInvoke.SetBkMode(_dc, BACKGROUND_MODE.TRANSPARENT);
            PInvoke.SetTextColor(_dc, new COLORREF(light ? 0x1A1A1Au : 0xFFFFFFu));
            var padding = (int)Math.Round(12 * scale);
            var bounds = new RECT { left = padding, top = 0, right = width - padding, bottom = height };
            fixed (char* chars = text)
            {
                PInvoke.DrawText(_dc, chars, text.Length, ref bounds, DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_END_ELLIPSIS | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            }

            ApplyRoundedAlpha(width, height, (int)Math.Round(6 * scale));
        }

        public void Dispose()
        {
            ReleaseFont();
            ReleaseBitmap();
            PInvoke.DeleteDC(_dc);
        }

        private void ApplyRoundedAlpha(int width, int height, int radius)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var coverage = Coverage(x, y, width, height, radius);
                    var pixel = _pixels[(y * width) + x] & 0x00FFFFFF;
                    if (coverage >= 1.0)
                    {
                        _pixels[(y * width) + x] = pixel | 0xFF000000;
                        continue;
                    }

                    var alpha = (uint)(coverage * 255);
                    var r = (((pixel >> 16) & 0xFF) * alpha) / 255;
                    var g = (((pixel >> 8) & 0xFF) * alpha) / 255;
                    var b = ((pixel & 0xFF) * alpha) / 255;
                    _pixels[(y * width) + x] = (alpha << 24) | (r << 16) | (g << 8) | b;
                }
            }
        }

        private static double Coverage(int x, int y, int width, int height, int radius)
        {
            var dx = x < radius ? radius - x - 0.5 : x >= width - radius ? x - (width - radius) + 0.5 : 0;
            var dy = y < radius ? radius - y - 0.5 : y >= height - radius ? y - (height - radius) + 0.5 : 0;
            if (dx <= 0 || dy <= 0)
            {
                return 1.0;
            }

            return Math.Clamp(radius - Math.Sqrt((dx * dx) + (dy * dy)) + 0.5, 0, 1);
        }

        private void EnsureBitmap(int width, int height)
        {
            if (!_bitmap.IsNull && _width == width && _height == height)
            {
                return;
            }

            ReleaseBitmap();
            var info = new BITMAPINFO();
            info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = width;
            info.bmiHeader.biHeight = -height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            void* bits;
            _bitmap = PInvoke.CreateDIBSection(_dc, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
            _pixels = (uint*)bits;
            _previousBitmap = PInvoke.SelectObject(_dc, _bitmap);
            _width = width;
            _height = height;
        }

        private void EnsureFont(double scale)
        {
            if (_font != IntPtr.Zero && _fontScale == scale)
            {
                return;
            }

            ReleaseFont();
            const uint defaultCharset = 1;
            const uint defaultPrecision = 0;
            const uint clearType = 5;
            _font = StripNative.CreateFont(-(int)Math.Round(14 * scale), 0, 0, 0, 600, 0, 0, 0, defaultCharset, defaultPrecision, defaultPrecision, clearType, 0, FontFace);
            _previousFont = PInvoke.SelectObject(_dc, new HGDIOBJ(_font));
            _fontScale = scale;
        }

        private void ReleaseFont()
        {
            if (_font == IntPtr.Zero)
            {
                return;
            }

            PInvoke.SelectObject(_dc, _previousFont);
            PInvoke.DeleteObject(new HGDIOBJ(_font));
            _font = IntPtr.Zero;
        }

        private void ReleaseBitmap()
        {
            if (_bitmap.IsNull)
            {
                return;
            }

            PInvoke.SelectObject(_dc, _previousBitmap);
            PInvoke.DeleteObject(_bitmap);
            _bitmap = default;
            _pixels = null;
        }
    }
}
