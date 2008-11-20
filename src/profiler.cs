using System.Diagnostics;
using System;

/** UNIMPORTANT */
public class Profiler
{
  public static bool GlobalPrintProfile = true;

  public Stopwatch Watch;
  public Print PrintProfile = Print.Global;
  public string Prefix;
  public double MinTime = 100.0;

  /** FAST */
  public Profiler () : this("") {}
  /** FAST */
  public Profiler (string prefix)
  {
    Prefix = prefix;
    Watch = new Stopwatch ();
    Watch.Start ();
  }

  /** FAST */
  public void Time (string message)
  {
    double elapsedMilliseconds = Watch.ElapsedTicks / 10000.0;
    if (
      (MinTime <= elapsedMilliseconds) &&
      ((PrintProfile == Print.Always) ||
      ((PrintProfile == Print.Global) && GlobalPrintProfile))
    ) {
      string time = String.Format ("{0} ms ", elapsedMilliseconds.ToString("N1"));
      Console.WriteLine (time.PadLeft(12) + Prefix + "  " + message);
    }
    Restart ();
  }

  public void Time (string format, object o1) {
    Time (String.Format(format, o1));
  }

  public void Time (string format, object o1, object o2) {
    Time (String.Format(format, o1, o2));
  }

  /** FAST */
  public void Restart () { Reset (); Start (); }
  /** FAST */
  public void Reset () { Watch.Reset (); }
  /** FAST */
  public void Start () { Watch.Start (); }
  /** FAST */
  public void Stop () { Watch.Stop (); }

  public enum Print {
    Global,
    Always,
    Never
  }
}
