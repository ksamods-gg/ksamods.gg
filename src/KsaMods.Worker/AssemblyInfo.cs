using System.Runtime.CompilerServices;

// The SSRF address check and the container flag list are internal because nothing outside the
// worker should call them - but they are exactly the two things that most need testing, since
// a silent regression in either is a security hole rather than a bug.
[assembly: InternalsVisibleTo("KsaMods.Tests")]
