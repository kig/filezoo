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

using System;
using System.Diagnostics;
using Gtk;
using Mono.Unix;

public static class FilezooApp {

  /**
    The Main method inits the Gtk application and creates a Filezoo instance
    to run.
  */
  static void Main (string[] args)
  {
    Window win;
    Profiler p = Helpers.StartupProfiler;
    p.Restart ();
    p.MinTime = 0;
    Profiler.GlobalPrintProfile = false;
    Helpers.ShowTextExtents = false;

    Catalog.Init("i18n","./locale");
    Application.Init ();

    p.Time ("Init done");

    bool panelMode = (Array.IndexOf (args, "--panel") > -1);
    args = Helpers.Without(args, "--panel");

    int panelBgIdx = Array.IndexOf(args, "--panel-bg");
    string panelBg = "";
    if (panelBgIdx > -1 && panelBgIdx != args.Length-1) {
      panelBg = args[panelBgIdx + 1];
      args = Helpers.Without (args, panelBg);
    }
    args = Helpers.Without (args, "--panel-bg");

    string dir = panelMode ? Helpers.HomeDir : ".";
    Filezoo fz = new Filezoo (dir);
    new FilezooConfig (args).Apply(fz);

    p.Time ("Created Filezoo");

    byte r, g, b;
    r = (byte)(fz.Renderer.BackgroundColor.R * 255);
    g = (byte)(fz.Renderer.BackgroundColor.G * 255);
    b = (byte)(fz.Renderer.BackgroundColor.B * 255);
    fz.ModifyBg (StateType.Normal, new Gdk.Color(r,g,b));

    if (panelMode) {
      win = new FilezooPanel (fz);
      if (panelBg.Length > 0) {
        Gdk.Color c = new Gdk.Color(0,0,0);
        if (Gdk.Color.Parse(panelBg, ref c)) {
          win.ModifyBg (StateType.Normal, c);
        } else {
          Console.WriteLine("Failed to parse panel bg color: {0}", panelBg);
        }
      }
    } else {
      win = new Window ("Filezoo");
      win.SetDefaultSize (420, 800);
      VBox vbox = new VBox (false, 0);
      FilezooControls controls = new FilezooControls(fz);
      vbox.PackStart (fz, true, true, 0);
      vbox.PackEnd (controls, false, false, 0);
      win.Add (vbox);
      win.ModifyBg (StateType.Normal, new Gdk.Color(r,g,b));
    }

    win.DeleteEvent += new DeleteEventHandler (OnQuit);
    win.ShowAll ();

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
