using Xunit;
using XTerm;
using XTerm.Options;
namespace Forterm.Tests;
public sealed class TerminalResizeModelTests
{
    [Fact] public void ClassicGridRestoresClippedTextColoursAndWideCellsWithoutNewRows()
    {
        using var t=new Terminal(new TerminalOptions{Cols=40,Rows=8,Scrollback=100,ReflowOnResize=false});
        t.Write("\x1b[31mPREFIX-中文-0123456789-TAIL\x1b[0m\r\nNEXT");string original=t.Buffer.GetLine(0)!.TranslateToString(true);int lines=t.Buffer.Length;
        t.Resize(8,5);Assert.Equal(lines,t.Buffer.Length);t.Resize(40,8);Assert.Equal(original,t.Buffer.GetLine(0)!.TranslateToString(true));Assert.Equal(2,t.Buffer.GetLine(0)![7].Width);
    }
    [Fact] public void ErasedClippedTailNeverReappearsOnGrow()
    {
        using var t=new Terminal(new TerminalOptions{Cols=40,Rows=8,ReflowOnResize=false});t.Write("OLD-TAIL-ABCDEFGHIJKLMNOPQRSTUVWXYZ");t.Resize(8,8);t.Write("\r\x1b[2KNEW");t.Resize(40,8);Assert.Equal("NEW",t.Buffer.GetLine(0)!.TranslateToString(true));
    }
    [Fact] public void EraseLeftDoesNotDiscardHiddenRightTail()
    {
        using var t=new Terminal(new TerminalOptions{Cols=30,Rows=6,ReflowOnResize=false});t.Write("0123456789-KEEP-RIGHT");t.Resize(8,6);t.Write("\r\x1b[7C\x1b[1K");t.Resize(30,6);Assert.Contains("89-KEEP-RIGHT",t.Buffer.GetLine(0)!.TranslateToString(true));
    }
    [Fact] public void SavedCursorTracksItsLineAcrossScrollbackHeightChanges()
    {
        using var t=new Terminal(new TerminalOptions{Cols=30,Rows=6,Scrollback=100,ReflowOnResize=false});for(int i=0;i<16;i++)t.Write($"row{i}\r\n");t.Write("\u001b7");int row=t.Buffer.YBase+t.Buffer.Y;
        t.Resize(30,12);t.Write("\u001b8ANCHOR");Assert.Equal(row,t.Buffer.YBase+t.Buffer.Y);Assert.Contains("ANCHOR",t.Buffer.GetLine(row)!.TranslateToString(true));
    }
    [Fact] public void SavedCursorAtPromptStartDoesNotMoveIntoNewWrappedRows()
    {
        using var t=new Terminal(new TerminalOptions{Cols=40,Rows=10,Scrollback=100});t.Write("\u001b7LONG-PROMPT-01234567890123456789\r\nINPUT> ");t.Resize(15,10);t.Write("\u001b8REPLACE");Assert.StartsWith("REPLACE",t.Buffer.GetLine(0)!.TranslateToString(true));
    }
    [Fact] public void SavedPendingWrapSurvivesNoChangeAndExpandsIntoNewColumns()
    {
        using var t=new Terminal(new TerminalOptions{Cols=4,Rows=5,ReflowOnResize=false});t.Write("ABCD\u001b7");t.Resize(8,5);t.Write("\u001b8E");Assert.StartsWith("ABCDE",t.Buffer.GetLine(0)!.TranslateToString(true));
    }
}
