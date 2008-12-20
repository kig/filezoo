using NUnit.Framework;
using System;

[TestFixture]
public class TestTest
{
  [Test]
  public void Pass()
  {
    string foo = "foo";
    Assert.AreEqual( "foo", foo );
    Assert.AreNotEqual( "bar", foo );
  }

  [Test]
  [Ignore("Oh who cares")]
  public void Ignore()
  {
    string foo = "foo";
    Assert.AreEqual( "bar", foo );
  }

  [Test]
  public void Fail()
  {
    string foo = "foo";
    Assert.AreEqual( "bar", foo );
  }

  [Test]
  [ExpectedException(typeof(ArgumentException))]
  public void Throw()
  {
    throw (new ArgumentException("oh what now", "foo"));
  }
}