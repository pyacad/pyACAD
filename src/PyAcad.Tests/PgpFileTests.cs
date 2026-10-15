using PyAcad.Runtime.Execution;
using Xunit;

namespace PyAcad.Tests
{
    public class PgpFileTests
    {
        [Fact]
        public void Names_reads_aliases_and_external_commands_and_skips_comments()
        {
            var names = PgpFile.Names(
                ";  Program Parameters File For AutoCAD\r\n" +
                "; CO, *COPY (commented out)\r\n" +
                "\r\n" +
                "NOTEPAD,   START NOTEPAD,  1,*File to edit: ,\r\n" +
                "3A,        *3DARRAY\r\n" +
                "co,        *COPY\r\n" +
                "   TR,     *TRIM\n" +
                "no comma here\n" +
                ", *EMPTY\n" +
                "TWO WORDS, *BAD\n");

            Assert.Equal(new[] { "3A", "CO", "NOTEPAD", "TR" }, new System.Collections.Generic.SortedSet<string>(names));
            Assert.Contains("co", names);
        }

        [Fact]
        public void Names_of_nothing_is_empty()
        {
            Assert.Empty(PgpFile.Names(null));
            Assert.Empty(PgpFile.Names(""));
        }
    }
}
