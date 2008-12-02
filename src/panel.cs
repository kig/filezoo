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

  public FilezooPanel (Window fzwin, Filezoo fz) : base ("Filezoo Panel")
  {
    FilezooWindow = fzwin;
    Fz = fz;
    FilezooWindow.Decorated = false;
    Decorated = false;
    Resizable = false;
    SkipPagerHint = true;
    SkipTaskbarHint = true;

    Button homeButton = new Button ("<span size=\"small\">Home</span>");
//     homeButton.Relief = ReliefStyle.None;
    ((Label)(homeButton.Children[0])).UseMarkup = true;
    homeButton.Clicked += new EventHandler (delegate {
      Go(Helpers.HomeDir); });

    Button dlButton = new Button ("<span size=\"small\">Downloads</span>");
//     dlButton.Relief = ReliefStyle.None;
    ((Label)(dlButton.Children[0])).UseMarkup = true;
    dlButton.Clicked += new EventHandler (delegate {
      Go(Helpers.HomeDir + Helpers.DirSepS + "downloads"); });

    Toggle = new ToggleButton (ToggleUp);
    ((Label)(Toggle.Children[0])).UseMarkup = true;
    Toggle.Clicked += new EventHandler (delegate {
      ToggleFilezoo (); });

    Entry entry = new Entry ();
    entry.Activated += new EventHandler (delegate {
      Go (entry.Text); });
    entry.WidthChars = 50;

    HBox hb = new HBox ();
    hb.Add (entry);
    hb.Add (dlButton);
    hb.Add (homeButton);
    hb.Add (Toggle);
//     hb.SetSizeRequest(-1, 25);

    Add (hb);
    Stick ();
  }

  void openUrl (string url) {
    if (url.StartsWith("http://") || url.StartsWith("www.")) {
      Helpers.OpenURL(url);
    } else if (url.StartsWith("?")) {
      Helpers.Search(url.Substring(1));
    } else if (url.StartsWith("!")) {
      Helpers.RunCommandInDir (url.Substring(1), "", Fz.CurrentDirPath);
    } else if (url.Contains(".") && !url.Contains(" ")) {
      Helpers.OpenURL(url);
    } else {
      Helpers.Search(url);
    }
  }

  void Go (string newDir) {
    if (newDir.StartsWith("~")) // tilde expansion
      newDir = Helpers.TildeExpand(newDir);
    if (newDir[0] != Helpers.DirSepC) { // relative path or fancy wacky stuff
      string hfd = Helpers.HomeDir + Helpers.DirSepS + newDir;
      if (!Helpers.FileExists(hfd))
        hfd = Fz.CurrentDirPath + Helpers.DirSepS + newDir;
      if (!Helpers.FileExists(hfd)) {
        openUrl (newDir);
        return;
      }
      newDir = hfd;
    }
    if (!Helpers.FileExists(newDir)) return;
    if (!Helpers.IsDir (newDir)) {
      Helpers.OpenFile (newDir);
      return;
    }
    string oldDir = Fz.CurrentDirPath;
    Fz.SetCurrentDir (newDir);
    if (!(FilezooWindow.IsMapped && oldDir != newDir))
      ToggleFilezoo ();
  }

  void ToggleFilezoo ()
  {
    if (FilezooWindow.IsMapped) {
      Toggle.Active = false;
      FilezooWindow.Hide ();
    } else {
      Toggle.Active = true;
      int x,y,w,h,mw,mh;
      GetPosition(out x, out y);
      GetSize (out mw, out mh);
      FilezooWindow.GetSize (out w, out h);
      w = Math.Max(w, mw);
      x = Math.Min (Screen.Width-w, x);
      x = Screen.Width-w;
      FilezooWindow.Resize (w, y);
      FilezooWindow.ShowAll ();
      FilezooWindow.Move (x, 0);
      FilezooWindow.Stick ();
    }
  }
}