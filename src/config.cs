using System;
using System.Collections.Generic;
using Cairo;
using Gtk;


public class FilezooConfig
{

  static Dictionary<string, string> Prefixes;
  string[] Args;

  public FilezooConfig (string[] args) {
    Args = args;
    Prefixes = new Dictionary<string, string> ();
    FillPrefixes ();
  }

  public void Apply (Filezoo fz) {
    string[] args = Args;

    fz.QuitAfterFirstFrame = (Array.IndexOf(args, "--quit") > -1);
    args = Helpers.Without(args, "--quit");

    if (args.Length > 0) fz.SetCurrentDir (args[args.Length - 1]);

    fz.Prefixes = Prefixes;

    fz.BreadcrumbFontFamily = "URW Gothic L";
    fz.ToolbarTitleFontFamily = "Sans";
    fz.ToolbarLabelFontFamily = "Sans";

    fz.FileNameFontFamily = "URW Gothic L";
    fz.FileInfoFontFamily = "Sans";

    Style s = Widget.DefaultStyle;

    fz.ActiveColor = ToColor(s.Foreground(StateType.Normal));

    fz.Renderer.BackgroundColor = ToColor(s.Background(StateType.Normal));
//     new Color (0.2, 0.2, 0.2);
//     fz.Renderer.BackgroundColor = new Color (0.2, 0.2, 0.2);

    fz.Renderer.DirectoryFGColor = ToColor(s.Foreground(StateType.Normal));
//     fz.Renderer.DirectoryBGColor = ToColor(s.Background(StateType.Normal));

//     fz.Renderer.DirectoryFGColor = new Color (0.6, 0.65, 0.7);
    fz.Renderer.DirectoryBGColor = new Color (0.8, 0.95, 1.0);
    fz.Renderer.UnfinishedDirectoryColor = new Color (0.455, 0.4, 1);

    fz.Renderer.RegularFileColor = new Color (0.188, 0.755, 1);
    fz.Renderer.SymlinkColor = new Color (0.655, 0.588, 0.855);
    fz.Renderer.ExecutableColor = new Color (0.4, 0.855, 0.3);
    fz.Renderer.BlockDeviceColor = new Color (0.855,0.655,0);
    fz.Renderer.CharacterDeviceColor = new Color (0.75,0.5,0);
    fz.Renderer.FifoColor = new Color (0.75,0.1,0.32);
    fz.Renderer.SocketColor = new Color (0.95,0.2,0.52);

//     fz.ActiveColor = fz.Renderer.RegularFileColor;
    fz.InActiveColor = fz.ActiveColor;
    fz.InActiveColor.A = 0.5;
  }

  Color ToColor (Gdk.Color c)
  {
    return new Color (
      ((double)c.Red) / 255.0,
      ((double)c.Blue) / 255.0,
      ((double)c.Green) / 255.0 );
  }

  void FillPrefixes () {
    Prefixes[".."] = "⇱";
    Prefixes["/dev"] = "⚠";
    Prefixes["/etc"] = "✦";
    Prefixes["/boot"] = "◉";
    Prefixes["/proc"] = Prefixes["/sys"] = "◎";
    Prefixes["/usr"] = Prefixes["/usr/local"] = "⬢";
    Prefixes["/usr/X11R6"] = "▤";
    Prefixes["/usr/src"] = Prefixes["/usr/local/src"] = "⚒";
    Prefixes["/bin"] = Prefixes["/usr/bin"] = Prefixes["/usr/local/bin"] = "⌬";
    Prefixes["/sbin"] = Prefixes["/usr/sbin"] = Prefixes["/usr/local/sbin"] = "⏣";
    Prefixes["/lib"] = Prefixes["/usr/lib"] = Prefixes["/usr/local/lib"] =
    Prefixes["/lib"] = Prefixes["/usr/X11R6/lib"] = Prefixes["/usr/X11R6/lib32"] =
    Prefixes["/lib32"] = Prefixes["/usr/lib32"] = Prefixes["/usr/local/lib32"] = "⬡";
    Prefixes["/include"] = Prefixes["/usr/include"] = Prefixes["/usr/local/include"] = "◌";
    Prefixes["/tmp"] = "⌚";
    Prefixes["/home"] = "⌂";
    Prefixes["/root"] = "♔";
    Prefixes["/usr/share"] = Prefixes["/usr/local/share"] = "✧";
    Prefixes["/var"] = "⚡";
    Prefixes["/var/run"] = "⚡";
    Prefixes["/var/backups"] = "❄";
    Prefixes["/var/cache"] = "♨";
    Prefixes["/var/crash"] = "☹";
    Prefixes["/var/games"] = "☺";
    Prefixes["/var/lock"] = "⚔";
    Prefixes["/var/mail"] = "✉";
    Prefixes["/var/spool"] = "✈";
    Prefixes["/var/tmp"] = "⌚";
    Prefixes["/var/lib"] = "✦";
    Prefixes["/var/log"] = "✇";
    Prefixes["/var/www"] = "⚓";
    Prefixes["/usr/games"] = Prefixes["/usr/local/games"] = "☺";
    Prefixes[Helpers.HomeDir] = "♜";
    Prefixes[Helpers.HomeDir+"/bin"] = "⌬";
    Prefixes[Helpers.HomeDir+"/code"] = "◌";
    Prefixes[Helpers.HomeDir+"/Trash"] =
    Prefixes[Helpers.HomeDir+"/.Trash"] = "♻";
    Prefixes[Helpers.HomeDir+"/downloads"] =
    Prefixes[Helpers.HomeDir+"/Downloads"] = "↴";
    Prefixes[Helpers.HomeDir+"/music"] =
    Prefixes[Helpers.HomeDir+"/Music"] = "♬";
    Prefixes[Helpers.HomeDir+"/Desktop"] = "▰";
    Prefixes[Helpers.HomeDir+"/documents"] =
    Prefixes[Helpers.HomeDir+"/Documents"] = "✎";
    Prefixes[Helpers.HomeDir+"/photos"] =
    Prefixes[Helpers.HomeDir+"/Photos"] =
    Prefixes[Helpers.HomeDir+"/pictures"] =
    Prefixes[Helpers.HomeDir+"/Pictures"] = "☐";
    Prefixes[Helpers.HomeDir+"/reading"] = "♾";
    Prefixes[Helpers.HomeDir+"/writing"] = "✍";
    Prefixes[Helpers.HomeDir+"/movies"] =
    Prefixes[Helpers.HomeDir+"/Movies"] =
    Prefixes[Helpers.HomeDir+"/logs"] = "✇";
    Prefixes[Helpers.HomeDir+"/video"] =
    Prefixes[Helpers.HomeDir+"/Video"] = "►";
    Prefixes[Helpers.HomeDir+"/public_html"] = "⚓";
  }

}