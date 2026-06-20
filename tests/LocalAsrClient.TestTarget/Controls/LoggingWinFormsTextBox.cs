using System.Windows.Forms;
using LocalAsrClient.TestTarget.Diagnostics;

namespace LocalAsrClient.TestTarget.Controls;

public sealed class LoggingWinFormsTextBox : TextBox
{
    private readonly TargetEventRecorder _recorder;

    public LoggingWinFormsTextBox(TargetEventRecorder recorder)
    {
        _recorder = recorder;
        Multiline = true;
        AcceptsReturn = true;
        Width = 420;
        Height = 90;
    }

    protected override void WndProc(ref Message m)
    {
        const int wmSetFocus = 0x0007;
        const int wmKillFocus = 0x0008;
        const int wmKeyDown = 0x0100;
        const int wmKeyUp = 0x0101;
        const int wmChar = 0x0102;
        const int wmSysKeyDown = 0x0104;
        const int wmSysKeyUp = 0x0105;
        const int wmPaste = 0x0302;

        if (m.Msg is wmSetFocus or wmKillFocus or wmKeyDown or wmKeyUp or wmChar or wmSysKeyDown or wmSysKeyUp or wmPaste)
        {
            _recorder.Record($"NativeTextBox.WM_0x{m.Msg:X4}", $"wParam=0x{m.WParam.ToInt64():X}");
        }

        base.WndProc(ref m);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        _recorder.Record("Target.NativeTextBox.TextChanged", $"length={Text.Length}");
        base.OnTextChanged(e);
    }
}
