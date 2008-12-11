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


public class FilezooPanel : Window
{
  Window FilezooWindow;
  Filezoo Fz;

  FilezooPanelControls Controls;

  public FilezooPanel (Filezoo fz) : base ("Filezoo Panel")
  {
    Fz = fz;
    Decorated = false;
    Resizable = false;
    SkipPagerHint = true;
    SkipTaskbarHint = true;

    FilezooWindow = new Window ("Filezoo");
    FilezooWindow.Decorated = false;
    FilezooWindow.Add (Fz);

    Controls = new FilezooPanelControls(Fz, FilezooWindow);

    Add (Controls);
    Stick ();

    Fz.Width = 400;
    Fz.Height = 1000;
    Fz.CompleteInit ();
  }
}


public class FilezooPanelControls : FilezooControls
{
  Window FilezooWindow;
  public ToggleButton Toggle;
  string ToggleUp = "<span size=\"small\">∆</span>";
//   string ToggleDown = "<span size=\"small\">∇</span>";

  public FilezooPanelControls (Filezoo fz, Window fzw) : base(fz)
  {
    FilezooWindow = fzw;

    Toggle = new ToggleButton (ToggleUp);
    ((Label)(Toggle.Children[0])).UseMarkup = true;
    Toggle.Clicked += delegate { ToggleFilezoo (); };

    PackEnd(Toggle, false, false, 0);

    FilezooWindow.DeleteEvent += delegate (object o, DeleteEventArgs e) {
      Toggle.Active = false;
      e.RetVal = true;
    };

    KeyReleaseEvent += delegate (object o, KeyReleaseEventArgs args) {
      if (args.Event.Key == Gdk.Key.Escape) {
        Toggle.Active = false;
      }
    };

    FilezooWindow.KeyReleaseEvent += delegate (object o, KeyReleaseEventArgs args) {
      if (args.Event.Key == Gdk.Key.Escape) {
        Toggle.Active = false;
      }
    };
  }

  override public void Go (string entry) {
    if (HandleEntry(entry)) {
      if (!FilezooWindow.IsMapped)
        ToggleFilezoo ();
      else
        FilezooWindow.GdkWindow.Raise ();
    }
  }

  void ToggleFilezoo ()
  {
    if (FilezooWindow.IsMapped) {
      Toggle.Active = false;
      FilezooWindow.Hide ();
    } else {
      Toggle.Active = true;
      int x,y,mw,mh;
      Parent.GdkWindow.GetPosition(out x, out y);
      Parent.GdkWindow.GetSize (out mw, out mh);
      x = Math.Min (Screen.Width-mw, x);
//       x = Screen.Width-mw;
      FilezooWindow.Resize (1, 1);
      FilezooWindow.ShowAll ();
      FilezooWindow.Move (x, 0);
      FilezooWindow.Resize (mw, y);
      FilezooWindow.Stick ();
    }
  }
}
