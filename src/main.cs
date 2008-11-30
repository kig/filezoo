/*
    Filezoo - a small and fast file manager
    Copyright (C) 2008  Ilmari Heikkinen

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using Gtk;
using System.Collections.Generic;

public static class FilezooApp {

  static Dictionary<string, string> _Prefixes = null;
  public static Dictionary<string, string> Prefixes
  { get {
    if (_Prefixes == null) {
      _Prefixes = new Dictionary<string, string> ();
      _Prefixes[".."] = "⇱";
      _Prefixes["/dev"] = "⚠";
      _Prefixes["/etc"] = "✦";
      _Prefixes["/boot"] = "◉";
      _Prefixes["/proc"] = _Prefixes["/sys"] = "◎";
      _Prefixes["/usr"] = _Prefixes["/usr/local"] = "⬢";
      _Prefixes["/usr/X11R6"] = "▤";
      _Prefixes["/usr/src"] = _Prefixes["/usr/local/src"] = "⚒";
      _Prefixes["/bin"] = _Prefixes["/usr/bin"] = _Prefixes["/usr/local/bin"] = "⌬";
      _Prefixes["/sbin"] = _Prefixes["/usr/sbin"] = _Prefixes["/usr/local/sbin"] = "⏣";
      _Prefixes["/lib"] = _Prefixes["/usr/lib"] = _Prefixes["/usr/local/lib"] =
      _Prefixes["/lib"] = _Prefixes["/usr/X11R6/lib"] = _Prefixes["/usr/X11R6/lib32"] =
      _Prefixes["/lib32"] = _Prefixes["/usr/lib32"] = _Prefixes["/usr/local/lib32"] = "⬡";
      _Prefixes["/include"] = _Prefixes["/usr/include"] = _Prefixes["/usr/local/include"] = "◌";
      _Prefixes["/tmp"] = "⌚";
      _Prefixes["/home"] = "⌂";
      _Prefixes["/root"] = "♔";
      _Prefixes["/usr/share"] = _Prefixes["/usr/local/share"] = "✧";
      _Prefixes["/var"] = "⚡";
      _Prefixes["/var/run"] = "⚡";
      _Prefixes["/var/backups"] = "❄";
      _Prefixes["/var/cache"] = "♨";
      _Prefixes["/var/crash"] = "☹";
      _Prefixes["/var/games"] = "☺";
      _Prefixes["/var/lock"] = "⚔";
      _Prefixes["/var/mail"] = "✉";
      _Prefixes["/var/spool"] = "✈";
      _Prefixes["/var/tmp"] = "⌚";
      _Prefixes["/var/lib"] = "✦";
      _Prefixes["/var/log"] = "✇";
      _Prefixes["/var/www"] = "⚓";
      _Prefixes["/usr/games"] = _Prefixes["/usr/local/games"] = "☺";
      _Prefixes[Helpers.HomeDir] = "♜";
      _Prefixes[Helpers.HomeDir+"/bin"] = "⌬";
      _Prefixes[Helpers.HomeDir+"/code"] = "◌";
      _Prefixes[Helpers.HomeDir+"/Trash"] =
      _Prefixes[Helpers.HomeDir+"/.Trash"] = "♻";
      _Prefixes[Helpers.HomeDir+"/downloads"] =
      _Prefixes[Helpers.HomeDir+"/Downloads"] = "↴";
      _Prefixes[Helpers.HomeDir+"/music"] =
      _Prefixes[Helpers.HomeDir+"/Music"] = "♬";
      _Prefixes[Helpers.HomeDir+"/Desktop"] = "▰";
      _Prefixes[Helpers.HomeDir+"/documents"] =
      _Prefixes[Helpers.HomeDir+"/Documents"] = "✎";
      _Prefixes[Helpers.HomeDir+"/photos"] =
      _Prefixes[Helpers.HomeDir+"/Photos"] =
      _Prefixes[Helpers.HomeDir+"/pictures"] =
      _Prefixes[Helpers.HomeDir+"/Pictures"] = "❏";
      _Prefixes[Helpers.HomeDir+"/reading"] = "♾";
      _Prefixes[Helpers.HomeDir+"/writing"] = "✍";
      _Prefixes[Helpers.HomeDir+"/movies"] =
      _Prefixes[Helpers.HomeDir+"/Movies"] =
      _Prefixes[Helpers.HomeDir+"/logs"] = "✇";
      _Prefixes[Helpers.HomeDir+"/video"] =
      _Prefixes[Helpers.HomeDir+"/Video"] = "►";
      _Prefixes[Helpers.HomeDir+"/public_html"] = "⚓";
    }
    return _Prefixes;
  } }


  /**
    The Main method inits the Gtk application and creates a Filezoo instance
    to run.
  */
  static void Main (string[] args)
  {
    Profiler p = Helpers.StartupProfiler;
    p.Restart ();
    p.MinTime = 0;
    Profiler.GlobalPrintProfile = true;
    Application.Init ();
    Window win = new Window ("Filezoo");
    win.SetDefaultSize (420, 800);
    p.Time ("Init done");
    System.Threading.ThreadPool.SetMinThreads (10, 20);
    Filezoo fz = new Filezoo (args.Length > 0 ? args[0] : ".");
    fz.Prefixes = Prefixes;
//     fz.QuitAfterFirstFrame = true;
    p.Time ("Created Filezoo");
    win.DeleteEvent += new DeleteEventHandler (OnQuit);
    p.Time ("Added delete handler");
    win.Add (fz);
    p.Time ("Added fz");
    win.ShowAll ();
    p.Time ("Gone ShowAll");
    p.Time ("Entering event loop");
    Application.Run ();
  }

  /**
    The quit event handler. Calls Application.Quit.
  */
  static void OnQuit (object sender, DeleteEventArgs e)
  {
    Application.Quit ();
  }
}
