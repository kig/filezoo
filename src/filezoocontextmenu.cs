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

using System.Diagnostics;
using System;
using Gtk;

public class FilezooContextMenu : Menu {

  Filezoo App;

  public FilezooContextMenu (Filezoo fz, ClickHit c) {
    App = fz;
    Build (this, c);
  }

  string[] exSuffixes = {"bz2", "gz", "rar", "tar", "zip"};

  public void Build (Menu menu, ClickHit c) {
    menu.Title = c.Target.FullName;
    string targetPath = c.Target.FullName;
    if (c.Target.IsDirectory) {
    // Directory menu items

      MenuItem goTo = new MenuItem ("_Go to " + c.Target.Name);
      goTo.Activated += new EventHandler(delegate {
        App.SetCurrentDir (targetPath); });
      menu.Append (goTo);

      if (targetPath != Helpers.RootDir) {
        MenuItem goToP = new MenuItem ("Go to _parent");
        goToP.Activated += new EventHandler(delegate {
          App.SetCurrentDir (Helpers.Dirname(targetPath)); });
        menu.Append (goToP);
      }

      MenuItem term = new MenuItem ("Open _terminal");
      term.Activated += new EventHandler(delegate {
        Helpers.OpenTerminal (targetPath); });
      menu.Append (term);

      /** DESTRUCTIVE */
      MenuItem create = new MenuItem ("Create _file...");
      create.Activated += new EventHandler(delegate {
        ShowCreateDialog (targetPath); });
      menu.Append (create);

    } else {
    // File menu items

      MenuItem open = new MenuItem ("_Open " + c.Target.Name);
      open.Activated += new EventHandler(delegate {
        Helpers.OpenFile (targetPath); });
      menu.Append (open);

      /** DESTRUCTIVE */
      if (Array.IndexOf (exSuffixes, c.Target.Suffix) > -1) {
        MenuItem ex = new MenuItem ("_Extract");
        ex.Activated += new EventHandler(delegate {
          Helpers.ExtractFile (targetPath); });
        menu.Append (ex);
      }

      MenuItem fterm = new MenuItem ("Open _terminal");
      fterm.Activated += new EventHandler(delegate {
        Helpers.OpenTerminal (Helpers.Dirname(targetPath)); });
      menu.Append (fterm);

    }

    menu.Append (new SeparatorMenuItem ());

    /** DESTRUCTIVE */
    MenuItem run = new MenuItem ("_Run command...");
    run.Activated += new EventHandler(delegate {
      ShowRunDialog (targetPath);
    });
    menu.Append (run);

    /** DESTRUCTIVE */
    MenuItem copy = new MenuItem ("_Copy to...");
    copy.Activated += new EventHandler(delegate {
      ShowCopyDialog (targetPath);
    });
    menu.Append (copy);

    /** DESTRUCTIVE */
    MenuItem rename = new MenuItem ("Re_name...");
    rename.Activated += new EventHandler(delegate {
      ShowRenameDialog (targetPath);
    });
    menu.Append (rename);

    /** DESTRUCTIVE */
    MenuItem trash = new MenuItem ("Move to trash");
    trash.Activated += new EventHandler(delegate {
      Helpers.Trash(targetPath);
      FSCache.Invalidate (targetPath);
    });
    menu.Append (trash);
  }

  void ShowRenameDialog (string path)
  {
    string basename = Helpers.Basename(path);
    Helpers.TextPrompt (
      "Renaming " + basename, String.Format ("Renaming {0}", path),
      path, "Rename",
      0, path.Length+Helpers.DirSepS.Length, -1,
      new Helpers.TextPromptHandler(delegate (string newPath) {
        if (path != newPath) {
          Helpers.Move (path, newPath);
          FSCache.Invalidate (path);
          FSCache.Invalidate (newPath);
          if (path == App.CurrentDirPath || newPath == App.CurrentDirPath) {
            App.SetCurrentDir (newPath);
          }
        }
      })
    );
  }

  void ShowCopyDialog (string path)
  {
    string basename = Helpers.Basename(path);
    Helpers.TextPrompt (
      "Copying " + basename, String.Format ("Copying {0}", path),
      path, "Copy",
      0, 0, -1,
      new Helpers.TextPromptHandler(delegate (string newPath) {
        if (path != newPath) {
          Helpers.Copy (path, newPath);
          FSCache.Invalidate (newPath);
        }
      })
    );
  }

  void ShowRunDialog (string path)
  {
    Helpers.TextPrompt (
      "Run command", "Enter command to run",
      " " + Helpers.EscapePath(path), "Run",
      0, 0, 0,
      new Helpers.TextPromptHandler(delegate (string newPath) {
        Process.Start(newPath);
      })
    );
  }

  void ShowCreateDialog (string path)
  {
    Helpers.TextPrompt (
      "Create file", "Create file",
      path + Helpers.DirSepS + "new_file", "Create",
      0, path.Length+Helpers.DirSepS.Length, -1,
      new Helpers.TextPromptHandler(delegate (string newPath) {
        Helpers.Touch (newPath);
        FSCache.Invalidate (newPath);
      }));
  }

}
