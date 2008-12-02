using System;
using Gtk;
using Mono.Unix;

public class FilezooPanel : Window
{
  Window FilezooWindow;
  Filezoo Fz;

  public FilezooPanel (Window fzwin, Filezoo fz) : base ("Filezoo Panel")
  {
    FilezooWindow = fzwin;
    Fz = fz;
    FilezooWindow.Decorated = false;
    Decorated = false;
    Resizable = false;
    SkipPagerHint = true;
    SkipTaskbarHint = true;

    Button homeButton = new Button ("Home");
//     homeButton.Relief = ReliefStyle.None;
    homeButton.Clicked += new EventHandler (delegate {
      Go(Helpers.HomeDir); });

    Button dlButton = new Button ("Downloads");
//     dlButton.Relief = ReliefStyle.None;
    dlButton.Clicked += new EventHandler (delegate {
      Go(Helpers.HomeDir + Helpers.DirSepS + "downloads"); });

    Entry entry = new Entry ();
    entry.Activated += new EventHandler (delegate {
      Go (entry.Text); });
    entry.WidthChars = 50;

    HBox hb = new HBox ();
    hb.Add (entry);
    hb.Add (dlButton);
    hb.Add (homeButton);
//     hb.SetSizeRequest(-1, 25);

    Add (hb);
    Stick ();
  }

  void openUrl (string url) {
    if (url.StartsWith("http://") || url.StartsWith("www.")) {
      Helpers.OpenURL(url);
    } else if (url.StartsWith("? ")) {
      Helpers.Search(url);
    } else if (url.Contains(".") && !url.Contains(" ")) {
      Helpers.OpenURL(url);
    } else {
      Helpers.Search(url);
    }
  }

  void Go (string newDir) {
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
      FilezooWindow.HideAll ();
    } else {
      int x,y,w,h,mw,mh;
      GetPosition(out x, out y);
      GetSize (out mw, out mh);
      FilezooWindow.GetSize (out w, out h);
      FilezooWindow.ShowAll ();
      x = Math.Min (Screen.Width-w, x);
      FilezooWindow.Move (x, 0);
      FilezooWindow.Resize (Math.Max(w, mw), y);
      FilezooWindow.Stick ();
    }
  }
}