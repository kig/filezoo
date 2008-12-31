/*
    priorityqueue.cs - a simple priority queue for strings
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

using System.Collections.Generic;
using System;

public class PriorityQueue
{
  List<PQEntry> Entries;

  public PriorityQueue ()
  {
    Entries = new List<PQEntry> ();
  }

  /**
    Enqueues a string with a priority.
    Large priorities are at the front of the queue while
    small priorities are at the end of the queue.
    */
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
      if (Entries.Count == 0) Clear ();
      return r;
    } else {
      throw new InvalidOperationException ("Can't Dequeue from an empty PriorityQueue");
    }
  }

  public void Clear ()
  {
    Entries = new List<PQEntry> ();
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
