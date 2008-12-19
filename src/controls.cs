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

public class FilezooControls : HBox
{
  protected Filezoo Fz;

  protected Entry entry;

  public FilezooControls (Filezoo fz) : base (false, 0)
  {
    Fz = fz;

    Button homeButton = new Button ("<span size=\"small\">Home</span>");
//     homeButton.Relief = ReliefStyle.None;
    ((Label)(homeButton.Children[0])).UseMarkup = true;
    homeButton.CanFocus = false;
    homeButton.Clicked += delegate { Go(Helpers.HomeDir); };

    Button dlButton = new Button ("<span size=\"small\">Downloads</span>");
//     dlButton.Relief = ReliefStyle.None;
    ((Label)(dlButton.Children[0])).UseMarkup = true;
    dlButton.CanFocus = false;
    dlButton.Clicked += delegate {
      Go(Helpers.HomeDir + Helpers.DirSepS + "downloads"); };

    entry = new Entry ();
    entry.WidthChars = 50;
    entry.Activated += delegate {
      Go (entry.Text);
      entry.Text = "";
    };
    EntryCompletion ec = new EntryCompletion ();
    ec.InlineCompletion = true;
    ec.InlineSelection = true;
    ec.PopupSetWidth = true;
    ec.PopupSingleMatch = true;
    ec.TextColumn = 0;
    entry.Completion = ec;
    entry.Completion.Model = CreateCompletionModel ();
    entry.Focused += delegate {
      RecreateEntryCompletion ();
    };
    entry.KeyReleaseEvent += delegate (object o, KeyReleaseEventArgs args) {
      if (args.Event.Key == Gdk.Key.Escape) {
        Fz.Cancelled = true;
        if (Fz.Selection.Count > 0) {
          Fz.ClearSelection ();
          args.RetVal = true;
        }
      }
    };

    PackStart (entry, false, false, 0);
    PackStart (dlButton, false, false, 0);
    PackStart (homeButton, false, false, 0);
  }

  void openUrl (string url) {
    if (url.StartsWith("http://") || url.StartsWith("www.")) {
      Helpers.OpenURL(url);
    } else if (url.StartsWith("?")) {
      Helpers.Search(url.Substring(1));
    } else if (url.StartsWith("!")) {
      /** DESTRUCTIVE */
      Helpers.RunShellCommandInDir (url.Substring(1), "", Fz.CurrentDirPath);
    } else if (url.Contains(".") && !url.Contains(" ")) {
      Helpers.OpenURL(url);
    } else if (url.StartsWith("cd ")) { // heh
      HandleEntry (url.Substring(3));
    } else if (Helpers.IsValidCommandLine(url)) {
      /** DESTRUCTIVE */
      Helpers.RunShellCommandInDir (url, "", Fz.CurrentDirPath);
    }
  }

  protected bool HandleEntry (string newDir) {
    newDir = newDir.Trim(' ');
    if (newDir.Length == 0) return true;
    if (newDir.StartsWith("~")) // tilde expansion
      newDir = Helpers.TildeExpand(newDir);
    if (newDir.Trim(' ') == "..") {
      if (Fz.CurrentDirPath == Helpers.RootDir) return true;
      newDir = Helpers.Dirname(Fz.CurrentDirPath);
    }
    if (newDir[0] != Helpers.DirSepC) { // relative path or fancy wacky stuff
      string hfd = Fz.CurrentDirPath + Helpers.DirSepS + newDir;
      if (!Helpers.FileExists(hfd))
        hfd = Helpers.HomeDir + Helpers.DirSepS + newDir;
      if (!Helpers.FileExists(hfd)) {
        openUrl (newDir);
        return false;
      }
      newDir = hfd;
    }
    if (!Helpers.FileExists(newDir)) return true;
    if (!Helpers.IsDir (newDir)) {
      Fz.OpenFile (newDir);
      return false;
    }
    Fz.SetCurrentDir (newDir);
    RecreateEntryCompletion ();
    return true;
  }

  public virtual void Go (string entry) {
    HandleEntry(entry);
  }

  void RecreateEntryCompletion ()
  {
    ListStore om = (ListStore)entry.Completion.Model;
    entry.Completion.Model = CreateCompletionModel ();
    // Console.WriteLine("Created completion model");
    om.Dispose ();
  }

  TreeModel CreateCompletionModel ()
  {
    ListStore store = new ListStore (typeof (string));
    if (Fz.CurrentDirPath != null)
      foreach (UnixFileSystemInfo f in Helpers.EntriesMaybe(Fz.CurrentDirPath))
        store.AppendValues (f.Name);
    return store;
  }
}