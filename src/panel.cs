using System;
using Gtk;
using Mono.Unix;

public class FilezooPanel : Window
{
  Window FilezooWindow;
  Filezoo Fz;

  ToggleButton Toggle;

  string ToggleUp = "<span size=\"small\">∆</span>";
//   string ToggleDown = "<span size=\"small\">∇</span>";

  public FilezooPanel (Filezoo fz) : base ("Filezoo Panel")
  {
    Fz = fz;
    Decorated = false;
    Resizable = false;
    SkipPagerHint = true;
    SkipTaskbarHint = true;

    Button homeButton = new Button ("<span size=\"small\">Home</span>");
//     homeButton.Relief = ReliefStyle.None;
    ((Label)(homeButton.Children[0])).UseMarkup = true;
    homeButton.Clicked += delegate { Go(Helpers.HomeDir); };

    Button dlButton = new Button ("<span size=\"small\">Downloads</span>");
//     dlButton.Relief = ReliefStyle.None;
    ((Label)(dlButton.Children[0])).UseMarkup = true;
    dlButton.Clicked += delegate {
      Go(Helpers.HomeDir + Helpers.DirSepS + "downloads"); };

    Toggle = new ToggleButton (ToggleUp);
    ((Label)(Toggle.Children[0])).UseMarkup = true;
    Toggle.Clicked += delegate { ToggleFilezoo (); };

    Entry entry = new Entry ();
    entry.WidthChars = 50;
    entry.Activated += delegate {
      Go (entry.Text);
      entry.Text = "";
    };

    HBox hb = new HBox ();
    hb.Add (entry);
    hb.Add (dlButton);
    hb.Add (homeButton);
    hb.Add (Toggle);
//     hb.SetSizeRequest(-1, 25);

    Add (hb);
    Stick ();

    KeyReleaseEvent += delegate (object o, KeyReleaseEventArgs args) {
      if (args.Event.Key == Gdk.Key.Escape) {
        Toggle.Active = false;
      }
    };

    FilezooWindow = new Window ("Filezoo");
    FilezooWindow.DeleteEvent += delegate (object o, DeleteEventArgs e) {
      Toggle.Active = false;
      e.RetVal = true;
    };
    FilezooWindow.Decorated = false;
    FilezooWindow.Add (Fz);

    Fz.Width = 400;
    Fz.Height = 1000;
    Fz.CompleteInit ();

  }

  void openUrl (string url) {
    if (url.StartsWith("http://") || url.StartsWith("www.")) {
      Helpers.OpenURL(url);
    } else if (url.StartsWith("?")) {
      Helpers.Search(url.Substring(1));
    } else if (url.StartsWith("!")) {
      Helpers.RunCommandInDir ("sh", "-c "+Helpers.EscapePath(url.Substring(1)), Fz.CurrentDirPath);
    } else if (url.Contains(".") && !url.Contains(" ")) {
      Helpers.OpenURL(url);
    } else if (Helpers.IsPlausibleCommandLine(url, Fz.CurrentDirPath)) {
      Helpers.RunCommandInDir ("sh", "-c "+ Helpers.EscapePath(url), Fz.CurrentDirPath);
    } else {
      Helpers.Search(url);
    }
  }

  bool HandleEntry (string newDir) {
    if (newDir.Length == 0) return true;
    if (newDir.StartsWith("~")) // tilde expansion
      newDir = Helpers.TildeExpand(newDir);
    if (newDir.Trim(' ') == "..") {
      if (Fz.CurrentDirPath == Helpers.RootDir) return true;
      newDir = Helpers.Dirname(Fz.CurrentDirPath);
    }
    if (newDir[0] != Helpers.DirSepC) { // relative path or fancy wacky stuff
      string hfd = Fz.CurrentDirPath + Helpers.DirSepS + newDir;
//       if (!Helpers.FileExists(hfd))
//         hfd = Helpers.HomeDir + Helpers.DirSepS + newDir;
      if (!Helpers.FileExists(hfd)) {
        openUrl (newDir);
        return false;
      }
      newDir = hfd;
    }
    if (!Helpers.FileExists(newDir)) return true;
    if (!Helpers.IsDir (newDir)) {
      Helpers.OpenFile (newDir);
      return false;
    }
    Fz.SetCurrentDir (newDir);
    return true;
  }

  void Go (string entry) {
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
      GetPosition(out x, out y);
      GetSize (out mw, out mh);
//       x = Math.Min (Screen.Width-mw, x);
      x = Screen.Width-mw;
      FilezooWindow.Resize (mw, y);
      FilezooWindow.ShowAll ();
      FilezooWindow.Move (x, 0);
      FilezooWindow.Stick ();
    }
  }
}