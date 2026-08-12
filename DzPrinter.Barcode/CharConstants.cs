namespace DzPrinter.Barcode;

/// <summary>
/// 字符常量定义。对应 JS SDK 中 <c>l</c> 冻结对象。
/// 仅保留条码模块实际使用到的字符码点。
/// </summary>
internal static class CharConstants
{
    public const int NullCharacter = 0;
    public const int MaxAsciiCharacter = 127;
    public const int LineFeed = 10;
    public const int LF = 10;
    public const int CarriageReturn = 13;
    public const int CR = 13;
    public const int Space = 32;
    public const int Underscore = 95;
    public const int Dollar = 36;
    public const int Num0 = 48;  // '0'
    public const int Num9 = 57;  // '9'
    public const int LowerA = 97;
    public const int LowerZ = 122;
    public const int UpperA = 65;
    public const int UpperZ = 90;
    public const int Ampersand = 38;
    public const int Asterisk = 42;
    public const int At = 64;
    public const int Backslash = 92;
    public const int Backtick = 96;
    public const int Bar = 124;
    public const int Caret = 94;
    public const int CloseBrace = 125;
    public const int CloseBracket = 93;
    public const int CloseParen = 41;
    public const int Colon = 58;
    public const int Comma = 44;
    public const int Dot = 46;
    public const int DoubleQuote = 34;
    public const int EqualSign = 61;
    public const int Exclamation = 33;
    public const int GreaterThan = 62;
    public const int Hash = 35;
    public const int LessThan = 60;
    public const int Minus = 45;
    public const int OpenBrace = 123;
    public const int OpenBracket = 91;
    public const int OpenParen = 40;
    public const int Percent = 37;
    public const int Plus = 43;
    public const int Question = 63;
    public const int Semicolon = 59;
    public const int SingleQuote = 39;
    public const int Slash = 47;
    public const int Tilde = 126;
    public const int Backspace = 8;
    public const int FormFeed = 12;
    public const int ByteOrderMark = 65279;
    public const int Tab = 9;
    public const int VerticalTab = 11;
}
