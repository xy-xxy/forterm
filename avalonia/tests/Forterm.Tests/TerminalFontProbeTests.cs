using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Forterm.Desktop;
using Iciclecreek.Terminal;
using Xunit;
namespace Forterm.Tests;
public sealed class TerminalFontProbeTests
{
    [AvaloniaFact] public void OwnedPowerlineSampleRecordsResolvedFontAndAvailableNerdFaces()
    {
        var terminal=SessionPane.CreateTerminal();var w=new Window{Width=800,Height=300,Content=terminal};w.Show();w.UpdateLayout();
        try
        {
            var view=terminal.GetVisualDescendants().OfType<TerminalView>().Single();var typeface=new Typeface(view.FontFamily).GlyphTypeface;bool glyph=typeface.CharacterToGlyphMap.TryGetGlyph(0xE0B0,out var id)&&id!=0;
            var candidates=FontManager.Current.SystemFonts.Where(f=>f.Name.Contains("Nerd",StringComparison.OrdinalIgnoreCase)||f.Name.Contains("Meslo",StringComparison.OrdinalIgnoreCase)).Select(f=>f.Name).ToArray();
            terminal.Terminal.Write("OWNED Unicode: 中文 e\u0301 \uE0B0 \uE0B2\r\n");
            File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath,"owned-font-probe.json"),JsonSerializer.Serialize(new{configured=view.FontFamily.ToString(),resolved=typeface.FamilyName,powerlineInPrimary=glyph,installedCandidates=candidates,charWidth=view.CharWidth,charHeight=view.CharHeight}));
            if(candidates.Any(n=>n is "MesloLGS NF" or "JetBrainsMono Nerd Font" or "Hack Nerd Font"))Assert.True(glyph);
            Assert.True(view.CharWidth>0);Assert.True(view.CharHeight>0);
        }
        finally{terminal.Dispose();w.Close();}
    }
}
