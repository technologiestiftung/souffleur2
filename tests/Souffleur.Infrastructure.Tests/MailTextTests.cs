using FluentAssertions;
using Souffleur.Infrastructure.Mail;

namespace Souffleur.Infrastructure.Tests;

/// <summary>HTML-Mails kommen als Text an: Absaetze bleiben Zeilen, Stil und Skript verschwinden.</summary>
public class MailTextTests
{
    [Fact]
    public void Html_wird_zu_lesbarem_Text()
    {
        const string html = """
            <html><head><style>p { color: red }</style></head>
            <body><p>Hallo <b>Welt</b>,</p><p>zweiter&nbsp;Absatz &amp; mehr.<br>Neue Zeile</p>
            <script>alert(1)</script><ul><li>eins</li><li>zwei</li></ul></body></html>
            """;

        string text = ImapMailbox.HtmlZuText(html);

        text.Should().Be("Hallo Welt,\nzweiter Absatz & mehr.\nNeue Zeile\neins\nzwei");
        text.Should().NotContain("color").And.NotContain("alert");
    }
}
