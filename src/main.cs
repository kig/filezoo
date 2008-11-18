using Gtk;

public static class FilezooApp {
  /**
    The Main method inits the Gtk application and creates a Filezoo instance
    to run.
  */
  static void Main (string[] args)
  {
    Profiler.GlobalPrintProfile = false;
    Application.Init ();
    Window win = new Window ("Filezoo");
    win.SetDefaultSize (400, 768);
    Filezoo fz = new Filezoo (args.Length > 0 ? args[0] : ".");
    win.DeleteEvent += new DeleteEventHandler (OnQuit);
    win.Add (fz);
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
