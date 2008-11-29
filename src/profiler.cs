using System.Diagnostics;
using System;

/** UNIMPORTANT */
public class Profiler
{
  public static bool GlobalPrintProfile = true;

  public Stopwatch Watch;
  public Print PrintProfile = Print.Global;
  public string Prefix;
  public double MinTime = 50.0;
  public double TotalElapsed = 0.0;

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
  public Profiler (string prefix, double minTime) : this(prefix)
  {
    MinTime = minTime;
  }

  void PrintTime (string message, double elapsedMilliseconds, string prefix)
  {
    if (
      (MinTime <= elapsedMilliseconds) &&
      ((PrintProfile == Print.Always) ||
      ((PrintProfile == Print.Global) && GlobalPrintProfile))
    ) {
      string time = String.Format ("{0} ms ", elapsedMilliseconds.ToString("N1"));
      string bar = "".PadLeft((int)Math.Min(10, Math.Round(elapsedMilliseconds / 20)), '#');
      Console.WriteLine (prefix + bar.PadLeft(10) + " " + time.PadLeft(10) + Prefix + "  " + message);
    }
  }

  /** FAST */
  public void Time (string message)
  {
    double elapsedMilliseconds = Watch.ElapsedTicks / 10000.0;
    TotalElapsed += elapsedMilliseconds;
    PrintTime (message, elapsedMilliseconds, "");
    Restart ();
  }

  public void Time (string format, object o1) {
    Time (String.Format(format, o1));
  }

  public void Time (string format, object o1, object o2) {
    Time (String.Format(format, o1, o2));
  }

  public void Total (string message) {
    PrintTime (message+"\n", TotalElapsed, "Total ".PadRight(50, '-')+"\n");
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
