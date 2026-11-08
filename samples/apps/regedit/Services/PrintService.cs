using System.Runtime.InteropServices;
using System.Text;
using ReactorRegedit.Core;

namespace ReactorRegedit.Services;

/// <summary>
/// Print (F18): the native print dialog chooses the printer, then keys are written in
/// regedit's text format to the printer DC with GDI (monospace, wrapped, paginated).
/// </summary>
internal static partial class PrintService
{
    /// <summary>Shows the print dialog. Returns null if the user cancelled.</summary>
    public static PrintJob? ChoosePrinter(nint owner)
    {
        var pd = new PRINTDLGW
        {
            lStructSize = (uint)Marshal.SizeOf<PRINTDLGW>(),
            hwndOwner = owner,
            Flags = PD_RETURNDC | PD_NOSELECTION | PD_NOPAGENUMS | PD_USEDEVMODECOPIESANDCOLLATE | PD_HIDEPRINTTOFILE,
            nCopies = 1,
        };
        var ok = PrintDlgW(ref pd);
        if (pd.hDevMode != 0) GlobalFree(pd.hDevMode);
        if (pd.hDevNames != 0) GlobalFree(pd.hDevNames);
        if (!ok)
        {
            var err = CommDlgExtendedError();
            if (pd.hDC != 0) DeleteDC(pd.hDC);
            if (err == 0) return null;
            throw new IOException(err is PDERR_NODEFAULTPRN or PDERR_NODEVICES
                ? "No printers are installed."
                : $"The print dialog failed (error 0x{err:x4}).");
        }
        if (pd.hDC == 0) throw new IOException("The printer could not be opened.");
        return new PrintJob(pd.hDC);
    }

    internal sealed class PrintJob(nint hdc) : IDisposable
    {
        private nint _hdc = hdc;

        /// <summary>Prints the subtrees; returns the number of keys printed. Throws <see cref="OperationCanceledException"/> after aborting the job.</summary>
        public int Print(string documentName, IEnumerable<string> roots, CancellationToken ct, Action<int, string>? progress, List<string>? skipped)
        {
            var name = Marshal.StringToHGlobalUni(documentName);
            try
            {
                var di = new DOCINFOW { cbSize = Marshal.SizeOf<DOCINFOW>(), lpszDocName = name };
                if (StartDocW(_hdc, ref di) <= 0)
                    throw new IOException($"The print job could not be started (error {Marshal.GetLastPInvokeError()}).");
                int keys = 0;
                using var writer = new PrinterTextWriter(_hdc);
                try
                {
                    var text = new RegTextWriter(writer);
                    RegFileService.Walk(roots, (path, lastWrite, values) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        text.WriteKey(path, lastWrite, values);
                    }, ct, path => progress?.Invoke(++keys, path), skipped);
                    writer.Finish();
                    if (EndDoc(_hdc) <= 0) throw new IOException("The print job could not be completed.");
                    return keys;
                }
                catch
                {
                    AbortDoc(_hdc);
                    throw;
                }
            }
            finally { Marshal.FreeHGlobal(name); }
        }

        public void Dispose()
        {
            if (_hdc != 0) { DeleteDC(_hdc); _hdc = 0; }
        }
    }

    /// <summary>Line-oriented TextWriter that renders to a printer DC page by page.</summary>
    private sealed class PrinterTextWriter : TextWriter
    {
        private readonly nint _hdc;
        private readonly nint _font;
        private readonly int _left, _top, _bottom, _lineHeight, _maxChars;
        private readonly StringBuilder _line = new();
        private int _y;
        private bool _pageOpen;

        public PrinterTextWriter(nint hdc)
        {
            _hdc = hdc;
            int dpiX = GetDeviceCaps(hdc, LOGPIXELSX), dpiY = GetDeviceCaps(hdc, LOGPIXELSY);
            int width = GetDeviceCaps(hdc, HORZRES), height = GetDeviceCaps(hdc, VERTRES);
            _left = dpiX / 2;
            _top = dpiY / 2;
            _bottom = height - dpiY / 2;
            _font = CreateFontW(-(9 * dpiY / 72), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, FIXED_PITCH | FF_MODERN, "Consolas");
            var old = SelectObject(hdc, _font);
            var tm = new byte[64];
            GetTextMetricsW(hdc, tm);
            SelectObject(hdc, old);
            int tmHeight = BitConverter.ToInt32(tm, 0), external = BitConverter.ToInt32(tm, 16), avg = BitConverter.ToInt32(tm, 20);
            _lineHeight = Math.Max(1, tmHeight + external);
            _maxChars = Math.Max(20, (width - 2 * _left) / Math.Max(1, avg));
        }

        public override Encoding Encoding => Encoding.Unicode;

        public override void Write(char value)
        {
            if (value == '\n') EmitLine();
            else if (value != '\r') _line.Append(value);
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            foreach (var c in value) Write(c);
        }

        public void Finish()
        {
            if (_line.Length > 0) EmitLine();
            if (_pageOpen && EndPage(_hdc) <= 0) throw new IOException("The printer reported an error.");
            _pageOpen = false;
        }

        private void EmitLine()
        {
            var s = _line.ToString();
            _line.Clear();
            if (s.Length == 0) { Advance(null); return; }
            for (int i = 0; i < s.Length; i += _maxChars)
                Advance(s.Substring(i, Math.Min(_maxChars, s.Length - i)));
        }

        private void Advance(string? text)
        {
            if (!_pageOpen || _y + _lineHeight > _bottom)
            {
                if (_pageOpen && EndPage(_hdc) <= 0) throw new IOException("The printer reported an error.");
                if (StartPage(_hdc) <= 0) throw new IOException("The printer reported an error.");
                _pageOpen = true;
                _y = _top;
                SelectObject(_hdc, _font); // some drivers reset the DC on StartPage
                if (text is null) return; // no blank line at the top of a page
            }
            if (text is not null) TextOutW(_hdc, _left, _y, text, text.Length);
            _y += _lineHeight;
        }

        protected override void Dispose(bool disposing)
        {
            if (_font != 0)
            {
                SelectObject(_hdc, GetStockObject(SYSTEM_FONT)); // a selected font can't be deleted
                DeleteObject(_font);
            }
            base.Dispose(disposing);
        }
    }

    // ── Native ───────────────────────────────────────────────────────

    private const uint PD_NOSELECTION = 0x4, PD_NOPAGENUMS = 0x8, PD_RETURNDC = 0x100,
        PD_USEDEVMODECOPIESANDCOLLATE = 0x40000, PD_HIDEPRINTTOFILE = 0x100000;
    private const uint PDERR_NODEVICES = 0x1007, PDERR_NODEFAULTPRN = 0x1008;
    private const int HORZRES = 8, VERTRES = 10, LOGPIXELSX = 88, LOGPIXELSY = 90, SYSTEM_FONT = 13;
    private const uint FIXED_PITCH = 1, FF_MODERN = 0x30;

    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTDLGW
    {
        public uint lStructSize;
        public nint hwndOwner, hDevMode, hDevNames, hDC;
        public uint Flags;
        public ushort nFromPage, nToPage, nMinPage, nMaxPage, nCopies;
        public nint hInstance, lCustData, lpfnPrintHook, lpfnSetupHook, lpPrintTemplateName, lpSetupTemplateName, hPrintTemplate, hSetupTemplate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DOCINFOW
    {
        public int cbSize;
        public nint lpszDocName, lpszOutput, lpszDatatype;
        public uint fwType;
    }

    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintDlgW(ref PRINTDLGW pd);

    [LibraryImport("comdlg32.dll")]
    private static partial uint CommDlgExtendedError();

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint hMem);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int StartDocW(nint hdc, ref DOCINFOW di);

    [LibraryImport("gdi32.dll")]
    private static partial int StartPage(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int EndPage(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int EndDoc(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int AbortDoc(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDeviceCaps(nint hdc, int index);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    private static partial nint GetStockObject(int index);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTextMetricsW(nint hdc, [Out] byte[] tm);

    [LibraryImport("gdi32.dll", EntryPoint = "TextOutW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TextOutW(nint hdc, int x, int y, string text, int length);
}
