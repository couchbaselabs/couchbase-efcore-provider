using Couchbase.EntityFrameworkCore.Storage.Internal;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class DotNetToGoDateFormatConverterTests
{
    [Fact]
    public void Convert_DefaultFormat_ProducesExpectedGoLayout()
    {
        // Must reproduce CBEF-23's originally hardcoded Fmt constant byte-for-byte -- this is the
        // regression guard proving the configurable-format refactor didn't change already-shipped,
        // live-verified behavior.
        var result = DotNetToGoDateFormatConverter.Convert("yyyy-MM-ddTHH:mm:ss.FFFK");

        Assert.Equal("2006-01-02T15:04:05.999Z07:00", result);
    }

    [Fact]
    public void Convert_DateOnlyFormat_ProducesExpectedGoLayout()
    {
        var result = DotNetToGoDateFormatConverter.Convert("yyyy-MM-dd");

        Assert.Equal("2006-01-02", result);
    }

    [Theory]
    [InlineData("yyyy", "2006")]
    [InlineData("MM", "01")]
    [InlineData("dd", "02")]
    [InlineData("HH", "15")]
    [InlineData("mm", "04")]
    [InlineData("ss", "05")]
    [InlineData("K", "Z07:00")]
    public void Convert_EachSupportedToken_MapsCorrectly(string dotNetToken, string expectedGoToken)
    {
        Assert.Equal(expectedGoToken, DotNetToGoDateFormatConverter.Convert(dotNetToken));
    }

    [Theory]
    [InlineData("f", "0")]
    [InlineData("ff", "00")]
    [InlineData("fff", "000")]
    [InlineData("fffffff", "0000000")]
    [InlineData("F", "9")]
    [InlineData("FF", "99")]
    [InlineData("FFF", "999")]
    [InlineData("FFFFFFF", "9999999")]
    public void Convert_FractionalSecondsTokens_MapToMatchingLengthRun(string dotNetToken, string expectedGoToken)
    {
        Assert.Equal(expectedGoToken, DotNetToGoDateFormatConverter.Convert(dotNetToken));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("-")]
    [InlineData(":")]
    [InlineData(".")]
    public void Convert_LiteralSeparators_PassThroughUnchanged(string literal)
    {
        Assert.Equal(literal, DotNetToGoDateFormatConverter.Convert(literal));
    }

    [Fact]
    public void Convert_QuotedLiteralT_ProducesSameResultAsUnquotedT()
    {
        // "yyyy-MM-dd'T'HH:mm:ss" is a very common way to write this exact ISO-8601 pattern.
        // DateTime.ToString strips the quotes and emits just "T", so the quotes must NOT be
        // passed through as literal characters into the Go layout -- if they were, N1QL would
        // never match against the actual (unquoted) stored data.
        var quoted = DotNetToGoDateFormatConverter.Convert("yyyy-MM-dd'T'HH:mm:ss");
        var unquoted = DotNetToGoDateFormatConverter.Convert("yyyy-MM-ddTHH:mm:ss");

        Assert.Equal("2006-01-02T15:04:05", quoted);
        Assert.Equal(unquoted, quoted);
    }

    [Fact]
    public void Convert_QuotedLiteralLetter_TreatedAsLiteralNotToken()
    {
        // A quoted 'y' must be emitted as a literal character, not interpreted as (part of) the
        // year token -- proving quoting actually suppresses token interpretation rather than just
        // being ignored.
        Assert.Equal("2006y", DotNetToGoDateFormatConverter.Convert("yyyy'y'"));
    }

    [Fact]
    public void Convert_DoubleQuotedLiteral_AlsoSuppressesTokenInterpretation()
    {
        Assert.Equal("2006Z", DotNetToGoDateFormatConverter.Convert("yyyy\"Z\""));
    }

    [Fact]
    public void Convert_BackslashEscape_ProducesLiteralCharacterOutsideQuotes()
    {
        Assert.Equal("2006'", DotNetToGoDateFormatConverter.Convert(@"yyyy\'"));
    }

    [Fact]
    public void Convert_BackslashEscape_WorksInsideQuotedLiteral()
    {
        Assert.Equal("2006Z'y", DotNetToGoDateFormatConverter.Convert("yyyy'Z\\'y'"));
    }

    [Fact]
    public void Convert_LoneQuote_ThrowsUnterminatedLiteral()
    {
        // A bare, unmatched quote must be rejected rather than silently passed through -- passing
        // it through (the pre-fix behavior) would have silently mis-converted any format using a
        // quoted literal section, e.g. "yyyy-MM-dd'T'HH:mm:ss".
        var ex = Assert.Throws<ArgumentException>(() => DotNetToGoDateFormatConverter.Convert("'"));
        Assert.Contains("unterminated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_UnterminatedQuotedLiteral_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => DotNetToGoDateFormatConverter.Convert("yyyy'T"));
        Assert.Contains("unterminated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_TrailingBackslash_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => DotNetToGoDateFormatConverter.Convert(@"yyyy\"));
        Assert.Contains("trailing escape", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_TrailingBackslashInsideQuotedLiteral_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => DotNetToGoDateFormatConverter.Convert(@"'abc\"));
        Assert.Contains("trailing escape", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("y")]      // wrong length for the year token
    [InlineData("yy")]
    [InlineData("M")]      // wrong length for the month token
    [InlineData("d")]      // wrong length for the day token (ambiguous with day-name tokens)
    [InlineData("ddd")]    // day name -- not supported
    [InlineData("h")]      // 12-hour clock -- not supported
    [InlineData("tt")]     // AM/PM designator -- not supported
    [InlineData("zzz")]    // separate offset specifier -- not supported (use K instead)
    [InlineData("ffffffff")] // 8 f's exceeds .NET's own max of 7
    public void Convert_UnsupportedToken_Throws(string unsupportedFormat)
    {
        var ex = Assert.Throws<ArgumentException>(() => DotNetToGoDateFormatConverter.Convert(unsupportedFormat));
        Assert.Contains(unsupportedFormat, ex.Message);
    }

    [Fact]
    public void Convert_MonthAndMinuteTokens_AreCaseSensitivelyDistinct()
    {
        // 'M' (month) and 'm' (minute) must not be confused with each other.
        Assert.Equal("01", DotNetToGoDateFormatConverter.Convert("MM"));
        Assert.Equal("04", DotNetToGoDateFormatConverter.Convert("mm"));
    }
}
