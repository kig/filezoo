using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System;

[TestFixture]
public class HelpersTest
{

  static string testDir = "/tmp/filezoo_test/";

  [SetUp]
  public void setup ()
  {
    if (Helpers.FileExists(testDir))
      Helpers.Delete(testDir);
    Helpers.MkdirP(testDir);
  }

  [TearDown]
  public void teardown ()
  {
    Helpers.Delete(testDir);
  }

  [Test]
  public void MkdirPSimple ()
  {
    Helpers.MkdirP(testDir + "mkdir_p");
    Assert.IsTrue(Helpers.FileExists(testDir + "mkdir_p"));
  }

  [Test]
  public void MkdirPFancyFilenames ()
  {
    Helpers.MkdirP(testDir+"mkdir_p/foo/bar/baz/");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p/foo/bar/baz"));
    Helpers.MkdirP(testDir+"mkdir_p");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p"));
    Helpers.MkdirP(testDir+"mkdir_p/foo and \nbar");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p/foo and \nbar"));
    Helpers.MkdirP(testDir+"mkdir_p/foo and \nbar\x8f\xf0");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p/foo and \nbar\x8f\xf0"));
  }

  [Test]
  public void MkdirPEvilFilenames ()
  {
    Helpers.MkdirP(testDir+"mkdir_p/:h#i:t!h[e]\n\t \rr(e\\");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p/:h#i:t!h[e]\n\t \rr(e\\"));
  }

  [Test]
  public void Touch ()
  {
    var l = new List<string> { "a", "b", "c", ":h#i:t!h[e]\n\t \rr(e\\" };
    l.ForEach(s => {
      Helpers.Touch(testDir+s);
      Assert.IsTrue(Helpers.FileExists(testDir+s));
    });
    Thread.Sleep(1000);
    l.ForEach(s => {
      var td1 = DateTime.Now.Subtract(Helpers.LastModified(testDir+s));
      Assert.Greater( td1.TotalMilliseconds, 1000 );
      Helpers.Touch(testDir+s);
      var td = DateTime.Now.Subtract(Helpers.LastModified(testDir+s));
      Assert.Less( td.TotalMilliseconds, 1000 );
    });
  }

  [Test]
  public void Trash ()
  {
    string td = Helpers.TrashDir + Helpers.DirSepS;
    if (Helpers.FileExists(td+"trashTest"))
      Helpers.Delete(td);

    Helpers.Touch(testDir+"trashTest");
    Helpers.Trash(testDir+"trashTest");
    Assert.IsTrue(Helpers.FileExists(td+"trashTest"));
    Helpers.Delete(td+"trashTest");

    Helpers.MkdirP(testDir+"trashTest");
    Helpers.Trash(testDir+"trashTest");
    Assert.IsTrue(Helpers.FileExists(td+"trashTest"));
    Helpers.Delete(td+"trashTest");
  }

  [Test]
  public void Delete ()
  {
    string tfn = testDir+"deleteTest";
    if (Helpers.FileExists(tfn))
      Helpers.Delete(tfn);
    Assert.IsFalse(Helpers.FileExists(tfn));
    Helpers.Touch(tfn);
    Helpers.Delete(tfn);
    Assert.IsFalse(Helpers.FileExists(tfn));
    Helpers.MkdirP(tfn);
    Helpers.Delete(tfn);
    Assert.IsFalse(Helpers.FileExists(tfn));
  }

  [Test]
  public void Move ()
  {
    string t1 = testDir+"moveTest1";
    string t2 = testDir+"moveTest2";
    Helpers.Delete(t1);
    Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.Move (t1, t2);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsFalse(Helpers.FileExists(t1));
    Helpers.Move (t2, t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
    Helpers.MkdirP(t2);
    Helpers.Move (t2, t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
  }

  [Test]
  public void MoveOverwrite ()
  {
    string t1 = testDir+"moveTest1";
    string t2 = testDir+"moveTest2";
    Helpers.Delete(t1);
    Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.Touch(t2);
    Helpers.Move (t1, t2);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsFalse(Helpers.FileExists(t1));
    Assert.IsTrue(Helpers.FileExists(Helpers.TrashDir+Helpers.DirSepS + "moveTest2"));
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest2");
    Helpers.MkdirP(t1);
    Helpers.Move (t2, t1);
    Assert.IsTrue(Helpers.FileExists(Helpers.TrashDir+Helpers.DirSepS + "moveTest1"));
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest1");
  }

//   public void Move (string src, string dst)
//   public void Move (string src, string dst, bool deleteOverwrite)
//   public void Copy (string src, string dst)
//   public void NewFileWith (string path, byte[] data)
//   public void ReplaceFileWith (string path, byte[] data)
//   public void AppendToFile (string path, byte[] data)
//   public void CopyURI (string src, string dst)
//   public void MoveURI (string src, string dst)
//   public void CopyURIs (string[] src, string dst)
//   public void MoveURIs (string[] src, string dst)
//   public void XferURIs
//   public ImageSurface GetThumbnail (string path)
//   public void ExtractFile (string path)

}

