using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class CsvTests
{
    [Fact]
    public void Quoted_fields_keep_commas_quotes_and_line_breaks()
    {
        var text = "name,full_text\r\nAboleth,\"Line one, still one\nLine \"\"two\"\"\"\nGoblin,plain\n";

        var rows = Csv.Read(new StringReader(text));

        rows.Should().HaveCount(2);
        rows[0]["name"].Should().Be("Aboleth");
        rows[0]["full_text"].Should().Be("Line one, still one\nLine \"two\"");
        rows[1]["full_text"].Should().Be("plain");
    }

    [Fact]
    public void A_row_with_the_wrong_number_of_fields_is_rejected()
    {
        var act = () => Csv.Read(new StringReader("a,b\n1,2,3\n"));

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void An_unterminated_quote_is_rejected()
    {
        var act = () => Csv.Read(new StringReader("a\n\"open\n"));

        act.Should().Throw<FormatException>();
    }
}
