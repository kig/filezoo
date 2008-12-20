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
using System.Linq;
using System;
using Gtk;
using Mono.Unix;

public class FilezooContextMenu : Menu {

  Filezoo App;

  public FilezooContextMenu (Filezoo fz, ClickHit c) {
    App = fz;
    Build (this, c);
  }

  public static List<string> archiveSuffixes = new List<string>() {"bz2", "gz", "rar", "tar", "zip"};
  public static List<string> audioSuffixes = new List<string>() {"mp3", "m4a", "ogg", "flac", "wav", "pls", "m3u"};
  public static List<string> videoSuffixes = new List<string>() {"mp4", "mkv", "ogv", "ogm", "divx", "gif", "avi", "mov", "mpg"};
  public static List<string> imageSuffixes = new List<string>() {"jpg", "jpeg", "png", "gif"};

  public void Build (Menu menu, ClickHit c) {
    menu.Title = c.Target.FullName;
    if (App.Selection.Count > 0) {
      BuildSelectionMenu (menu, c);
      Menu smenu = new Menu ();
      AddItem (menu, c.Target.Name.Replace("_", "__"), smenu);
      menu = smenu;
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

    string selFormat = App.Selection.ContainsKey(c.Target.FullName) ? "Deselect {0}" : "Select {0}";
    AddItem (menu, String.Format(selFormat, c.Target.Name.Replace("_", "__")), delegate {
      App.ToggleSelection (c.Target.FullName);
    });

    UnixDirectoryInfo u = new UnixDirectoryInfo (c.Target.FullName);
    if (u.GetEntries(@"^\.git$").Length > 0) {
      Separator (menu);
      AddItem (menu, "View in gitk", delegate {
        Helpers.RunCommandInDir ("gitk", "", targetPath);
      });
    }

    if (HasEntryWithSuffix(u, imageSuffixes)) {
      Separator (menu);
      AddCommandItem(menu, "View images", "gqview", "", targetPath);
    }

    Separator(menu);
    AddCommandItem(menu, "Set as playlist", "amarok", "-p --load", targetPath);
    AddCommandItem(menu, "Append to playlist", "amarok", "--append", targetPath);
    Separator(menu);
    AddCommandItem(menu, "Recursive slideshow", "slideshow", "--recursive", targetPath);
  }

  // File menu items
  void BuildFileMenu (Menu menu, ClickHit c)
  {
    string targetPath = c.Target.FullName;

    string selFormat = App.Selection.ContainsKey(c.Target.FullName) ? "Deselect {0}" : "Select {0}";
    AddItem (menu, String.Format(selFormat, c.Target.Name.Replace("_", "__")), delegate {
      App.ToggleSelection (c.Target.FullName);
    });

    Separator (menu);

    AddItem (menu, "Open " + c.Target.Name.Replace("_", "__"), delegate {
      Helpers.OpenFile (targetPath);
    });

    if (videoSuffixes.Contains (c.Target.Suffix)) {
      AddCommandItem(menu, "Play video", "mplayer", "", targetPath);
    }

    if (imageSuffixes.Contains (c.Target.Suffix)) {
      AddCommandItem(menu, "View image", "gqview", "", targetPath);
      Separator(menu);
      AddCommandItem(menu, "Rotate ↱", "mogrify", "-rotate 90", targetPath, true);
      AddCommandItem(menu, "Rotate ↰", "mogrify", "-rotate 270", targetPath, true);
      AddCommandItem(menu, "Rotate 180°", "mogrify", "-rotate 180", targetPath, true);
    }

    if (audioSuffixes.Contains (c.Target.Suffix)) {
      AddCommandItem(menu, "Play audio", "amarok", "-p --load", targetPath);
      AddCommandItem(menu, "Append to playlist", "amarok", "--append", targetPath);
    }

    /** DESTRUCTIVE */
    if (archiveSuffixes.Contains (c.Target.Suffix)) {
      AddItem (menu, "_Extract", delegate { Helpers.ExtractFile (targetPath); });
    }

    if ((c.Target.Permissions & FileAccessPermissions.UserExecute) != 0) {
      AddItem (menu, "Run " + c.Target.Name, delegate {
        Helpers.RunCommandInDir (targetPath, "", Helpers.Dirname(targetPath));
      });
    }
  }

  void BuildCommonMenu (Menu menu, ClickHit c)
  {
    string targetPath = c.Target.FullName;
    string targetDir = c.Target.IsDirectory ? targetPath : Helpers.Dirname(targetPath);

    if (App.Selection.Count == 0)
      BuildCopyPasteMenu (menu, c);
    else
      Separator (menu);

    /** DESTRUCTIVE */
    AddItem (menu, "Re_name…", delegate {
      ShowRenameDialog (targetPath);
    });

    /** DESTRUCTIVE */
    AddItem (menu, "Touch", delegate {
      Helpers.Touch (targetPath);
    });

    Separator (menu);

    /** DESTRUCTIVE */
    AddItem (menu, "Create _file…", delegate { ShowCreateDialog (targetDir); });


    /** DESTRUCTIVE */
    AddItem (menu, "Create _directory…", delegate {
      ShowCreateDirDialog (targetDir);
    });

    Separator (menu);

    /** DESTRUCTIVE */
    AddItem (menu, "Move to trash", delegate {
      Helpers.Trash(targetPath);
      FSCache.Invalidate (targetPath);
    });

    Separator (menu);

    /** DESTRUCTIVE */
    AddItem (menu, "_Run command…", delegate {
      ShowRunDialog (targetPath);
    });

    AddItem (menu, "Open _terminal", delegate {
      Helpers.OpenTerminal (targetDir);
    });

//    /** DESTRUCTIVE */
//     AddItem (menu, "_Copy to…", delegate {
//       ShowCopyDialog (targetPath);
//     });

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
//     string selFormat = App.Selection.ContainsKey(c.Target.FullName) ? "Deselect {0}" : "Select {0}";
    AddItem (menu, "Toggle selection", delegate {
      App.ToggleSelection (c.Target.FullName);
    });

    string targetDir = c.Target.IsDirectory ? c.Target.FullName : Helpers.Dirname(c.Target.FullName);

    IEnumerable<string> keys = App.Selection.Keys;

    if (HasEntryWithSuffix(keys, videoSuffixes)) {
      Separator (menu);
      AddMultiArgCommandItem(menu, "Play selected videos", "mplayer", "", keys, videoSuffixes);
    }

    if (HasEntryWithSuffix(keys, imageSuffixes)) {
      Separator (menu);
      AddMultiArgCommandItem(menu, "View selected images", "gqview", "", keys, imageSuffixes);
      Separator(menu);
      AddMultiArgCommandItem(menu, "Recursive slideshow", "slideshow", "--recursive", keys);
      Separator (menu);
      AddCommandItem(menu, "Rotate selected images ↱", "mogrify", "-rotate 90", keys, imageSuffixes, true);
      AddCommandItem(menu, "Rotate selected images ↰", "mogrify", "-rotate 270", keys, imageSuffixes, true);
      AddCommandItem(menu, "Rotate selected images 180°", "mogrify", "-rotate 180", keys, imageSuffixes, true);
    } else if (keys.Any(Helpers.IsDir)) {
      Separator(menu);
      AddMultiArgCommandItem(menu, "Recursive slideshow", "slideshow", "--recursive", keys);
    }

    if (HasEntryWithSuffix(keys, audioSuffixes) || keys.Any(Helpers.IsDir)) {
      Separator (menu);
      AddMultiArgCommandItem(menu, "Set selection as playlist", "amarok", "-p --load", keys);
      AddMultiArgCommandItem(menu, "Append selection to playlist", "amarok", "--append", keys);
    }

    /** DESTRUCTIVE */
    if (HasEntryWithSuffix(keys, archiveSuffixes)) {
      Separator (menu);
      AddItem (menu, "_Extract selected archives", new EventHandler(delegate {
        var targets = keys.Where(path => archiveSuffixes.Contains(Helpers.Extname(path).ToLower()));
        foreach(string path in targets)
          Helpers.ExtractFile(path);
      }));
    }


    Separator (menu);

    AddItem (menu, "Clear selection", delegate { App.ClearSelection (); });

    Separator (menu);

    /** DESTRUCTIVE */
    AddItem (menu, String.Format("_Move selected to {0}/", Helpers.Basename(targetDir).Replace("_", "__")),
      delegate {
        App.MoveSelectionTo(targetDir);
    });

    /** DESTRUCTIVE */
    AddItem (menu, String.Format("_Copy selected to {0}/", Helpers.Basename(targetDir).Replace("_", "__")),
      delegate {
        App.CopySelectionTo(targetDir);
    });

    Separator (menu);

    /** DESTRUCTIVE */
    AddItem (menu, "Move selected to trash", delegate {
      App.TrashSelection ();
    });

    BuildCopyPasteMenu (menu, c);
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
  { AddCommandItem(menu,title,cmd,args,path,false); }
  public static void AddCommandItem (Menu menu, string title, string cmd, string args, string path, bool touch)
  {
    AddItem(menu, title, delegate {
      Helpers.RunCommandInDir (cmd, args + " " + Helpers.EscapePath(path), Helpers.Dirname(path));
      if (touch) {
        FSCache.Invalidate(path);
      }
    });
  }

  public static void AddCommandItem
  (Menu menu, string title, string cmd, string args, IEnumerable<string> paths, IEnumerable<string> suffixes)
  { AddCommandItem(menu, title, cmd, args, paths, suffixes, false); }

  public static void AddCommandItem
  (Menu menu, string title, string cmd, string args, IEnumerable<string> paths, IEnumerable<string> suffixes, bool touch)
  {
    AddItem(menu, title, delegate {
      IEnumerable<string> targets;
      if (suffixes == null) targets = paths;
      else targets = paths.Where(path => suffixes.Contains(Helpers.Extname(path).ToLower()));
/*      string filenames = String.Join(" ", targets.Select(o => Helpers.EscapePath(o)).ToArray ());
      Console.WriteLine(filenames);*/
      foreach(string path in targets) {
        Helpers.RunCommandInDir (cmd, args + " " + Helpers.EscapePath(path), Helpers.Dirname(path));
        if (touch) {
          FSCache.Invalidate(path);
        }
      }
    });
  }

  public static void AddMultiArgCommandItem
  (Menu menu, string title, string cmd, string args, IEnumerable<string> paths)
  { AddMultiArgCommandItem(menu, title, cmd, args, paths, null); }

  public static void AddMultiArgCommandItem
  (Menu menu, string title, string cmd, string args, IEnumerable<string> paths, IEnumerable<string> suffixes)
  {
    AddItem(menu, title, new EventHandler(delegate {
      IEnumerable<string> targets;
      if (suffixes == null) targets = paths;
      else targets = paths.Where(path => suffixes.Contains(Helpers.Extname(path).ToLower()));
      if (targets.Count() > 0) {
        string fp = targets.First ();
        string filenames = String.Join(" ", targets.Select(o => Helpers.EscapePath(o)).ToArray ());
//         Console.WriteLine(filenames);
        Helpers.RunCommandInDir (cmd, args + " " + filenames, Helpers.Dirname(fp));
      }
    }));
  }


  /* Filesystem helpers */

  public static bool HasEntryWithSuffix (UnixDirectoryInfo u, IEnumerable<string> suffixes)
  {
    foreach (UnixFileSystemInfo f in Helpers.EntriesMaybe(u))
      if (suffixes.Contains(Helpers.Extname(f.FullName).ToLower ()))
        return true;
    return false;
  }

  public static bool HasEntryWithSuffix (IEnumerable<string> paths, IEnumerable<string> suffixes)
  {
    return paths.Any(path => suffixes.Contains(Helpers.Extname(path).ToLower()));
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
        Helpers.RunShellCommandInDir(newPath, "", Helpers.IsDir(path) ? path : Helpers.Dirname(path));
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
