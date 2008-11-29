using System.Collections.Generic;
using System;

public class PriorityQueue
{
  List<PQEntry> Entries;

  public PriorityQueue ()
  {
    Entries = new List<PQEntry> ();
  }

  public void Enqueue (string value, int priority)
  {
    int i=0;
    for (i=0; i<Entries.Count; i++)
      if (Entries[i].Priority < priority) break;
    Entries.Insert(i, new PQEntry (value, priority));
  }

  public string Dequeue ()
  {
    if (Entries.Count > 0) {
      string r = Entries[0].Value;
      Entries.RemoveAt(0);
      return r;
    } else {
      throw new InvalidOperationException ("Can't Dequeue from an empty PriorityQueue");
    }
  }

  public void Clear ()
  {
    Entries.Clear ();
  }

  public int Count { get { return Entries.Count; } }


}

public struct PQEntry
{
  public string Value;
  public int Priority;
  public PQEntry (string v, int p)
  {
    Value = v;
    Priority = p;
  }
}
