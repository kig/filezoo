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
using Gtk;
using Mono.Unix;

public static class FilezooApp {

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

    bool panelMode = (Array.IndexOf (args, "--panel") > -1);

    Catalog.Init("i18n","./locale");
    System.Threading.ThreadPool.SetMinThreads (10, 20);
    Application.Init ();
    Window win = new Window ("Filezoo");
    win.SetDefaultSize (420, 800);

    p.Time ("Init done");

    Filezoo fz = new Filezoo (args.Length > 0 ? args[0] : ".");
    new FilezooConfig ().Apply(fz);

    p.Time ("Created Filezoo");

    win.Add (fz);
    if (panelMode)
      win = new FilezooPanel (win, fz);

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
