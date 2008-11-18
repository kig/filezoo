using System.Diagnostics;
using System;

public class Profiler
{
  public static bool GlobalPrintProfile = true;

  protected Stopwatch Watch;
  public Print PrintProfile = Print.Global;

  public Profiler ()
  {
    Watch = new Stopwatch ();
    Watch.Start ();
  }

  public void Time (string message)
  {
    double elapsedMilliseconds = Watch.ElapsedTicks / 10000.0;
    if (
      (PrintProfile == Print.Always) ||
      ((PrintProfile == Print.Global) && GlobalPrintProfile)
    ) {
      string time = String.Format ("{0} ms ", elapsedMilliseconds.ToString("N1"));
      Console.WriteLine (time.PadLeft(12) + message);
    }
    Restart ();
  }

  public void Restart () { Reset (); Start (); }
  public void Reset () { Watch.Reset (); }
  public void Start () { Watch.Start (); }
  public void Stop () { Watch.Stop (); }

  public enum Print {
    Global,
    Always,
    Never
  }
}
