// PAC Tools - what a spreadsheet column becomes in Revit, and whether a cell can get there.
//
// Every cell arrives as text. This file decides what that text means for the type the user chose, and -
// just as important - says plainly when it means nothing, so the cell can be reported rather than guessed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PacTools.Core
{
    /// <summary>The Revit parameter type a spreadsheet column is imported as.</summary>
    public enum ColumnDataType
    {
        /// <summary>Anything. Never fails. The default.</summary>
        Text = 0,

        /// <summary>A whole number.</summary>
        Integer,

        /// <summary>A unitless decimal number.</summary>
        Number,

        /// <summary>A length, with units.</summary>
        Length,

        /// <summary>An area, with units.</summary>
        Area,

        /// <summary>A tick box.</summary>
        YesNo
    }

    /// <summary>What a cell turned into, or why it could not.</summary>
    public sealed class ParsedCell
    {
        private ParsedCell() { }

        /// <summary>True when the value can be written to Revit.</summary>
        public bool Ok { get; private set; }

        /// <summary>True when the cell was blank. Blank is always allowed; the parameter is left empty.</summary>
        public bool IsBlank { get; private set; }

        /// <summary>The text to store, for a Text column.</summary>
        public string Text { get; private set; }

        /// <summary>The number, for Integer / Number / Length / Area.</summary>
        public double Number { get; private set; }

        /// <summary>The flag, for YesNo.</summary>
        public bool Flag { get; private set; }

        /// <summary>
        /// The unit found in the cell ("mm", "m", "sqm"), or null when none was written. Converting to
        /// Revit's internal units happens in <c>Revit/</c>, which is the only layer that knows about them.
        /// </summary>
        public string Unit { get; private set; }

        /// <summary>Why the cell could not be used, in words the user can act on. Null when <see cref="Ok"/>.</summary>
        public string Problem { get; private set; }

        internal static ParsedCell Blank() => new ParsedCell { Ok = true, IsBlank = true, Text = string.Empty };

        internal static ParsedCell OfText(string text) => new ParsedCell { Ok = true, Text = text };

        internal static ParsedCell OfNumber(double value, string unit, string text) =>
            new ParsedCell { Ok = true, Number = value, Unit = unit, Text = text };

        internal static ParsedCell OfFlag(bool value, string text) =>
            new ParsedCell { Ok = true, Flag = value, Text = text };

        internal static ParsedCell Bad(string problem) => new ParsedCell { Ok = false, Problem = problem };
    }

    /// <summary>Turns cell text into a typed value, or explains why it cannot.</summary>
    public static class ValueParser
    {
        private static readonly string[] TrueWords = { "yes", "y", "true", "t", "1", "x", "✓", "✔" };
        private static readonly string[] FalseWords = { "no", "n", "false", "f", "0", "-" };

        /// <summary>Unit suffixes recognised at the end of a number, longest first so "mm" beats "m".</summary>
        private static readonly string[] Units =
        {
            "sq.mm", "sq mm", "sqmm", "mm2", "mm²",
            "sq.m", "sq m", "sqm", "m2", "m²",
            "sq.ft", "sq ft", "sqft", "ft2", "ft²",
            "mm", "cm", "m", "km", "ft", "in", "\""
        };

        /// <summary>
        /// Reads <paramref name="raw"/> as <paramref name="type"/>. Blank is always accepted and reported
        /// as blank. Anything else that will not fit comes back with a message naming the value.
        /// </summary>
        public static ParsedCell Parse(string raw, ColumnDataType type)
        {
            string value = (raw ?? string.Empty).Trim();
            if (value.Length == 0) return ParsedCell.Blank();

            switch (type)
            {
                case ColumnDataType.Text:
                    return ParsedCell.OfText(value);

                case ColumnDataType.YesNo:
                    return ParseFlag(value);

                case ColumnDataType.Integer:
                    return ParseInteger(value);

                case ColumnDataType.Number:
                case ColumnDataType.Length:
                case ColumnDataType.Area:
                    return ParseNumber(value, type);

                default:
                    return ParsedCell.Bad("'" + value + "' cannot be imported: unknown column type.");
            }
        }

        private static ParsedCell ParseFlag(string value)
        {
            string lower = value.ToLowerInvariant();
            if (TrueWords.Contains(lower)) return ParsedCell.OfFlag(true, value);
            if (FalseWords.Contains(lower)) return ParsedCell.OfFlag(false, value);
            return ParsedCell.Bad("'" + value + "' is not a yes or no. Use Yes/No, True/False, Y/N or 1/0.");
        }

        private static ParsedCell ParseInteger(string value)
        {
            ParsedCell asNumber = ParseNumber(value, ColumnDataType.Number);
            if (!asNumber.Ok)
                return ParsedCell.Bad("'" + value + "' is not a whole number.");

            if (Math.Abs(asNumber.Number - Math.Round(asNumber.Number)) > 1e-9)
                return ParsedCell.Bad("'" + value + "' is not a whole number. Use a Number column for decimals.");

            if (asNumber.Number > int.MaxValue || asNumber.Number < int.MinValue)
                return ParsedCell.Bad("'" + value + "' is too large for a whole number.");

            return ParsedCell.OfNumber(Math.Round(asNumber.Number), null, value);
        }

        private static ParsedCell ParseNumber(string value, ColumnDataType type)
        {
            string text = value;
            bool negative = false;

            // Accounting negatives: (1,250) means -1250.
            if (text.Length > 2 && text[0] == '(' && text[text.Length - 1] == ')')
            {
                negative = true;
                text = text.Substring(1, text.Length - 2).Trim();
            }

            // A currency symbol in front is noise, not data.
            text = text.TrimStart('₹', '$', '£', '€').Trim();

            bool percent = text.EndsWith("%", StringComparison.Ordinal);
            if (percent) text = text.Substring(0, text.Length - 1).Trim();

            string unit = TakeUnit(ref text);
            text = text.Replace(" ", string.Empty).Replace(" ", string.Empty);

            // Accept both 1,234.5 and 1.234,5 - but only when it is unambiguous.
            if (!TryParseNumber(text, out double number))
            {
                string what = unit == null ? "'" + value + "' is not a number."
                                           : "'" + value + "' is not a number (the unit '" + unit + "' was recognised).";
                return ParsedCell.Bad(what);
            }

            if (percent) number /= 100.0;
            if (negative) number = -number;

            if (type == ColumnDataType.Number) unit = null;   // a plain Number carries no unit
            return ParsedCell.OfNumber(number, unit, value);
        }

        private static string TakeUnit(ref string text)
        {
            foreach (string u in Units)
            {
                if (text.Length <= u.Length) continue;
                if (!text.EndsWith(u, StringComparison.OrdinalIgnoreCase)) continue;
                text = text.Substring(0, text.Length - u.Length).Trim();
                return u;
            }
            return null;
        }

        private static bool TryParseNumber(string text, out double number)
        {
            number = 0;
            if (text.Length == 0) return false;

            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;

            // "1,234.56" - comma groups, dot decimal.
            if (double.TryParse(text, styles, CultureInfo.InvariantCulture, out number)) return true;

            // "1.234,56" - the European convention, when there is exactly one comma after the last dot.
            int lastComma = text.LastIndexOf(',');
            int lastDot = text.LastIndexOf('.');
            if (lastComma > lastDot)
            {
                string swapped = text.Replace(".", string.Empty).Replace(',', '.');
                if (double.TryParse(swapped, styles, CultureInfo.InvariantCulture, out number)) return true;
            }

            // Whatever the machine's own culture says, last.
            return double.TryParse(text, styles, CultureInfo.CurrentCulture, out number);
        }

        /// <summary>How the type reads in the mapping grid.</summary>
        public static string Label(ColumnDataType type)
        {
            switch (type)
            {
                case ColumnDataType.Text: return "Text";
                case ColumnDataType.Integer: return "Whole number";
                case ColumnDataType.Number: return "Number";
                case ColumnDataType.Length: return "Length";
                case ColumnDataType.Area: return "Area";
                case ColumnDataType.YesNo: return "Yes / No";
                default: return type.ToString();
            }
        }
    }

    /// <summary>Guesses what a column is, from the values in it. The user can always override the guess.</summary>
    public static class TypeGuess
    {
        /// <summary>
        /// Picks the narrowest type every non-blank value fits. A column that is entirely blank, or that
        /// has even one value no narrower type accepts, comes back as <see cref="ColumnDataType.Text"/>.
        /// </summary>
        public static ColumnDataType Guess(IEnumerable<string> values, int sampleSize = 200)
        {
            List<string> sample = (values ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Take(Math.Max(1, sampleSize))
                .ToList();

            if (sample.Count == 0) return ColumnDataType.Text;

            // Yes/No only when the column actually says so in words. "1" and "0" parse as a flag, but a
            // column of 1s and 0s is far more likely to be counts, and guessing wrong loses the numbers.
            bool anyWord = sample.Any(v => v.Trim().Any(char.IsLetter));
            if (anyWord && sample.All(v => ValueParser.Parse(v, ColumnDataType.YesNo).Ok))
                return ColumnDataType.YesNo;

            // Otherwise narrowest first; the first type that takes every value wins.
            foreach (ColumnDataType candidate in new[] { ColumnDataType.Integer, ColumnDataType.Number })
            {
                if (sample.All(v => ValueParser.Parse(v, candidate).Ok)) return candidate;
            }

            return ColumnDataType.Text;
        }
    }
}
