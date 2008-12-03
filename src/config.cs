
using System.Collections.Generic;
using Cairo;

public class FilezooConfig
{

  static Dictionary<string, string> Prefixes;

  public FilezooConfig () {
    Prefixes = new Dictionary<string, string> ();
    FillPrefixes ();
  }

  public void Apply (Filezoo fz) {
//     fz.QuitAfterFirstFrame = true;
    fz.Prefixes = Prefixes;

    fz.BreadcrumbFontFamily = "URW Gothic L";
    fz.ToolbarTitleFontFamily = "Sans";
    fz.ToolbarLabelFontFamily = "Sans";

    fz.FileNameFontFamily = "URW Gothic L";
    fz.FileInfoFontFamily = "Sans";

    fz.ActiveColor = new Color (0.188, 0.855, 1);
    fz.InActiveColor = new Color (0.188, 0.855, 1,0.5);

    fz.Renderer.BackgroundColor = new Color (0.2, 0.2, 0.2);
    fz.Renderer.RegularFileColor = new Color (0.188, 0.855, 1);
    fz.Renderer.SymlinkColor = new Color (0.855, 0.188, 1);
    fz.Renderer.DirectoryColor = new Color (0.6, 0.65, 0.7);
    fz.Renderer.UnfinishedDirectoryColor = new Color (0.855, 0.4, 1);
    fz.Renderer.ExecutableColor = new Color (0.4, 1.0, 0.6);
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
    Prefixes[Helpers.HomeDir+"/Pictures"] = "❏";
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