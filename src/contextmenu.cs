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
using System.Collections.Generic;
using System;
using Gtk;
using Mono.Unix;

public class FilezooContextMenu : Menu {

  Filezoo App;

  public FilezooContextMenu (Filezoo fz, ClickHit c) {
    App = fz;
    Build (this, c);
  }

  string[] exSuffixes = {"bz2", "gz", "rar", "tar", "zip"};
  string[] amarokSuffixes = {"mp3", "m4a", "ogg", "flac", "wav", "pls", "m3u"};
  string[] mplayerSuffixes = {"mp4", "mkv", "ogv", "ogm", "divx", "gif", "avi", "mov", "mpg"};
  string[] gqviewSuffixes = {"jpg", "jpeg", "png", "gif"};

  public void Build (Menu menu, ClickHit c) {
    menu.Title = c.Target.FullName;
    if (App.Selection.Count > 0) {
      BuildSelectionMenu (menu, c);
      Separator (menu);
    }
    if (c.Target.IsDirectory)
      BuildDirMenu (menu, c);
    else
      BuildFileMenu (menu, c);
    BuildCommonMenu (menu, c);
  }


  // Directory menu items
  void BuildDirMenu (Menu menu, ClickHit c)
  {
    string targetPath = c.Target.FullName;

    AddItem (menu, "_Go to " + c.Target.Name, delegate {
      App.SetCurrentDir (targetPath);
    });

    AddItem (menu, "Open _terminal", delegate { Helpers.OpenTerminal (targetPath); });

    /** DESTRUCTIVE */
    AddItem (menu, "Create _file…", delegate { ShowCreateDialog (targetPath); });


    /** DESTRUCTIVE */
    AddItem (menu, "Create _directory…", delegate {
      ShowCreateDirDialog (targetPath);
    });

    UnixDirectoryInfo u = new UnixDirectoryInfo (c.Target.FullName);
    if (u.GetEntries(@"^\.git$").Length > 0) {
      AddItem (menu, "Gitk", delegate {
        Helpers.RunCommandInDir ("gitk", "", targetPath);
      });
    }

    if (HasEntryWithSuffix(u, gqviewSuffixes)) {
      AddCommandItem(menu, "View images", "gqview", "", targetPath);
    }

    Menu audioMenu = new Menu ();
    AddCommandItem(audioMenu, "Set as playlist", "amarok", "-p --load", targetPath);
    AddCommandItem(audioMenu, "Append to playlist", "amarok", "--append", targetPath);
    AddItem (menu, "Audio", audioMenu);
  }

  // File menu items
  void BuildFileMenu (Menu menu, ClickHit c)
  {
    string targetPath = c.Target.FullName;

    AddItem (menu, "_Open " + c.Target.Name, delegate {
      Helpers.OpenFile (targetPath);
    });

    AddItem (menu, "Open _terminal", delegate {
      Helpers.OpenTerminal (Helpers.Dirname(targetPath));
    });

    if (Array.IndexOf (mplayerSuffixes, c.Target.Suffix) > -1) {
      AddCommandItem(menu, "Play video", "mplayer", "", targetPath);
    }

    if (Array.IndexOf (gqviewSuffixes, c.Target.Suffix) > -1) {
      AddCommandItem(menu, "View image", "gqview", "", targetPath);
    }

    if (Array.IndexOf (amarokSuffixes, c.Target.Suffix) > -1) {
      AddCommandItem(menu, "Play audio", "amarok", "-p --load", targetPath);
      AddCommandItem(menu, "Append to playlist", "amarok", "--append", targetPath);
    }

    /** DESTRUCTIVE */
    if (Array.IndexOf (exSuffixes, c.Target.Suffix) > -1) {
      AddItem (menu, "_Extract", delegate { Helpers.ExtractFile (targetPath); });
    }

    if ((c.Target.Permissions & FileAccessPermissions.UserExecute) != 0) {
      AddItem (menu, "Run", delegate {
        Helpers.RunCommandInDir (targetPath, "", Helpers.Dirname(targetPath));
      });
    }
  }

  void BuildCommonMenu (Menu menu, ClickHit c)
  {
    string targetPath = c.Target.FullName;

    BuildCopyPasteMenu (menu, c);

    /** DESTRUCTIVE */
    AddItem (menu, "_Run command…", delegate {
      ShowRunDialog (targetPath);
    });


    /** DESTRUCTIVE */
//     AddItem (menu, "_Copy to…", delegate {
//       ShowCopyDialog (targetPath);
//     });

    /** DESTRUCTIVE */
    AddItem (menu, "Re_name…", delegate {
      ShowRenameDialog (targetPath);
    });

    /** DESTRUCTIVE */
    AddItem (menu, "Move to trash", delegate {
      Helpers.Trash(targetPath);
      FSCache.Invalidate (targetPath);
    });
  }

  void BuildCopyPasteMenu (Menu menu, ClickHit c)
  {
    string targetPath = c.Target.FullName;

    Separator (menu);
    AddItem(menu, "Cut", delegate {
      App.CutSelection(targetPath);
    });
    AddItem(menu, "Copy", delegate {
      App.CopySelection(targetPath);
    });
    AddItem(menu, "Paste", delegate {
      App.PasteSelection(targetPath);
    });
    Separator (menu);
  }

  void BuildSelectionMenu (Menu menu, ClickHit c)
  {
    AddItem (menu, "Clear selection", delegate { App.ClearSelection (); });

    string targetDir = c.Target.IsDirectory ? c.Target.FullName : Helpers.Dirname(c.Target.FullName);

    /** DESTRUCTIVE */
    AddItem (menu, String.Format("_Move selected to {0}/", Helpers.Basename(targetDir)),
      delegate {
        App.MoveSelectionTo(targetDir);
    });

    /** DESTRUCTIVE */
    AddItem (menu, String.Format("_Copy selected to {0}/", Helpers.Basename(targetDir)),
      delegate {
        App.CopySelectionTo(targetDir);
    });

    /** DESTRUCTIVE */
    AddItem (menu, "Move selected to trash", delegate {
      App.TrashSelection ();
    });
  }


  /* GTK Menu helpers */

  public static void Separator (Menu menu)
  {
    menu.Append (new SeparatorMenuItem());
  }

  public static MenuItem MkItem (string title, EventHandler onActivate)
  {
    MenuItem m = new MenuItem (title);
    m.Activated += onActivate;
    return m;
  }

  public static MenuItem MkItem (string title, Menu subMenu)
  {
    MenuItem m = new MenuItem (title);
    m.Submenu = subMenu;
    return m;
  }

  public static void AddItem (Menu menu, string title, EventHandler onActivate)
  { menu.Append(MkItem(title, onActivate)); }

  public static void AddItem (Menu menu, string title, Menu subMenu)
  { menu.Append(MkItem(title, subMenu)); }

  public static void AddCommandItem (Menu menu, string title, string cmd, string args, string path)
  {
    AddItem(menu, title, delegate {
      Helpers.RunCommandInDir (cmd, args + " " + Helpers.EscapePath(path), Helpers.Dirname(path));
    });
  }


  /* Filesystem helpers */

  public static bool HasEntryWithSuffix (UnixDirectoryInfo u, string[] suffixes)
  {
    foreach (UnixFileSystemInfo f in Helpers.EntriesMaybe(u))
      if (Array.IndexOf(suffixes, Helpers.Extname(f.FullName).ToLower ()) > -1)
        return true;
    return false;
  }


  /* GTK dialog helpers */

  public void ShowRenameDialog (string path)
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

  public static void ShowCopyDialog (string path)
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

  public static void ShowRunDialog (string path)
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

  public static void ShowCreateDialog (string path)
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

  public static void ShowCreateDirDialog (string path)
  {
    Helpers.TextPrompt (
      "Create directory", "Create directory",
      path + Helpers.DirSepS + "new_directory", "Create",
      0, path.Length+Helpers.DirSepS.Length, -1,
      new Helpers.TextPromptHandler(delegate (string newPath) {
        Helpers.MkdirP (newPath);
        FSCache.Invalidate (newPath);
      }));
  }
}
