/*
    profiler.cs - simple profiler class for printing out execution times
    Copyright (C) 2008  Ilmari Heikkinen

    Permission is hereby granted, free of charge, to any person
    obtaining a copy of this software and associated documentation
    files (the "Software"), to deal in the Software without
    restriction, including without limitation the rights to use,
    copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the
    Software is furnished to do so, subject to the following
    conditions:

    The above copyright notice and this permission notice shall be
    included in all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
    OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
    NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
    HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
    WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
    FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
    OTHER DEALINGS IN THE SOFTWARE.
*/

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
    Restart ();
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
      int barLength = Math.Max((int)Math.Min(10, Math.Round(elapsedMilliseconds)), 0);
      string barStart = (elapsedMilliseconds > 10 ? "─" : (barLength < 1 ? "" : "╾"));
      string bar = barStart.PadRight(barLength, '─');
      Console.WriteLine (prefix + bar.PadLeft(10) + "╼ " + time.PadLeft(10) + Prefix + "  " + message);
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

  public void Total (string format, object o1) {
    Total (String.Format(format, o1));
  }
  public void Total (string format, object o1, object o2) {
    Total (String.Format(format, o1, o2));
  }
  public void Total (string message) {
    PrintTime (message, TotalElapsed, "Total: ");
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


public class MemoryProfiler
{
  public double CurrentMemory;
  public double StartMemory;

  public MemoryProfiler ()
  {
    CurrentMemory = StartMemory = GC.GetTotalMemory(false);
  }

  public void Memory (string message)
  {
    double memoryDelta = GC.GetTotalMemory(false) - CurrentMemory;
    CurrentMemory += memoryDelta;
    Console.WriteLine("Memory delta: {0}", memoryDelta);
  }

  public void Total (string message)
  {
    double memoryDelta = GC.GetTotalMemory(false) - StartMemory;
    Console.WriteLine("Total memory delta: {0}", memoryDelta);
  }
}