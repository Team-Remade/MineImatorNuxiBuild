using System.Globalization;

namespace MineImatorSimplyRemade.core.ui.Panels;

public static class NumericExpressionParser
{
    public static bool TryEvaluate(string text, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        int index = 0;
        if (!ParseExpression(text, ref index, out result))
            return false;

        SkipWhitespace(text, ref index);
        return index == text.Length;
    }

    public static bool HasExpressionSyntax(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (char c in text)
        {
            if (c == '+' || c == '-' || c == '*' || c == '/' || c == '(' || c == ')')
                return true;
        }

        return false;
    }

    public static string SanitizeText(string text, bool allowDecimal, bool allowExponent)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "0";

        var builder = new System.Text.StringBuilder(text.Length);
        bool hasDecimal = false;
        bool hasExponent = false;

        foreach (char c in text)
        {
            if (char.IsDigit(c))
            {
                builder.Append(c);
            }
            else if (c == '+' || c == '-' || c == '*' || c == '/' || c == '(' || c == ')')
            {
                builder.Append(c);
            }
            else if (allowDecimal && c == '.' && !hasDecimal && !hasExponent)
            {
                builder.Append(c);
                hasDecimal = true;
            }
            else if (allowExponent && (c == 'e' || c == 'E') && !hasExponent && builder.Length > 0)
            {
                builder.Append(c);
                hasExponent = true;
            }
        }

        string sanitized = builder.Length > 0 ? builder.ToString() : "0";
        if (HasExpressionSyntax(sanitized) && TryEvaluate(sanitized, out double parsedValue))
            return parsedValue.ToString(CultureInfo.InvariantCulture);

        return sanitized;
    }

    private static bool ParseExpression(string text, ref int index, out double value)
    {
        if (!ParseTerm(text, ref index, out value))
            return false;

        while (true)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
                return true;

            char c = text[index];
            if (c == '+')
            {
                index++;
                if (!ParseTerm(text, ref index, out double rhs))
                    return false;

                value += rhs;
            }
            else if (c == '-')
            {
                index++;
                if (!ParseTerm(text, ref index, out double rhs))
                    return false;

                value -= rhs;
            }
            else
            {
                return true;
            }
        }
    }

    private static bool ParseTerm(string text, ref int index, out double value)
    {
        if (!ParseUnary(text, ref index, out value))
            return false;

        while (true)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
                return true;

            char c = text[index];
            if (c == '*')
            {
                index++;
                if (!ParseUnary(text, ref index, out double rhs))
                    return false;

                value *= rhs;
            }
            else if (c == '/')
            {
                index++;
                if (!ParseUnary(text, ref index, out double rhs))
                    return false;

                if (rhs == 0)
                    return false;

                value /= rhs;
            }
            else
            {
                return true;
            }
        }
    }

    private static bool ParseUnary(string text, ref int index, out double value)
    {
        SkipWhitespace(text, ref index);
        if (index >= text.Length)
        {
            value = 0;
            return false;
        }

        char c = text[index];
        int sign = 1;
        if (c == '+')
        {
            sign = 1;
            index++;
        }
        else if (c == '-')
        {
            sign = -1;
            index++;
        }

        if (!ParsePrimary(text, ref index, out value))
            return false;

        value *= sign;
        return true;
    }

    private static bool ParsePrimary(string text, ref int index, out double value)
    {
        SkipWhitespace(text, ref index);
        if (index >= text.Length)
        {
            value = 0;
            return false;
        }

        char c = text[index];
        if (c == '(')
        {
            index++;
            if (!ParseExpression(text, ref index, out value))
                return false;

            SkipWhitespace(text, ref index);
            if (index >= text.Length || text[index] != ')')
                return false;

            index++;
            return true;
        }

        return ParseNumber(text, ref index, out value);
    }

    private static bool ParseNumber(string text, ref int index, out double value)
    {
        int start = index;
        bool hasDigit = false;

        while (index < text.Length && char.IsDigit(text[index]))
        {
            hasDigit = true;
            index++;
        }

        if (index < text.Length && text[index] == '.')
        {
            index++;
            while (index < text.Length && char.IsDigit(text[index]))
            {
                hasDigit = true;
                index++;
            }
        }

        if (!hasDigit)
        {
            value = 0;
            return false;
        }

        if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
        {
            int expIndex = index;
            index++;
            if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                index++;

            bool expHasDigit = false;
            while (index < text.Length && char.IsDigit(text[index]))
            {
                expHasDigit = true;
                index++;
            }

            if (!expHasDigit)
            {
                index = expIndex;
            }
        }

        string numberText = text.Substring(start, index - start);
        return double.TryParse(numberText, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
    }
}
