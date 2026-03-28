using System.Globalization;
using System.Numerics;
using System.Text;
using PwlEditor.Models;

namespace PwlEditor.Services.Formula;

public static class FormulaEngine
{
    public static IReadOnlyList<WavePoint> GeneratePoints(string expression, double periodDuration, int samplesPerPeriod, FormulaOutputMode outputMode)
    {
        if (periodDuration <= 0)
            throw new ArgumentException("Die Periodendauer muss größer als 0 sein.");

        if (samplesPerPeriod < 2)
            throw new ArgumentException("Mindestens 2 Samples pro Periode sind nötig.");

        var parser = new Parser(expression);
        var ast = parser.Parse();

        var result = new List<WavePoint>(samplesPerPeriod + 1);
        for (var i = 0; i <= samplesPerPeriod; i++)
        {
            var t = periodDuration * i / samplesPerPeriod;
            var value = ast.Evaluate(t);
            var y = outputMode switch
            {
                FormulaOutputMode.Real => value.Real,
                FormulaOutputMode.Imaginary => value.Imaginary,
                FormulaOutputMode.Magnitude => value.Magnitude,
                FormulaOutputMode.Phase => value.Phase,
                _ => value.Real
            };

            if (double.IsNaN(y) || double.IsInfinity(y))
                throw new InvalidOperationException($"Die Formel ergibt bei t={t.ToString(CultureInfo.InvariantCulture)} einen ungültigen Wert.");

            result.Add(new WavePoint(t, y));
        }

        return result;
    }

    public static string ToLatexPreview(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return string.Empty;

        var s = expression.Trim();
        s = s.Replace("pi", "\\pi", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("sin", "\\sin", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("cos", "\\cos", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("tan", "\\tan", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("exp", "\\exp", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("log10", "\\log_{10}", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("log", "\\log", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("sqrt", "\\sqrt", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("*", " \\cdot ");
        s = s.Replace("phase", "\\operatorname{phase}", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("real", "\\operatorname{real}", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("imag", "\\operatorname{imag}", StringComparison.OrdinalIgnoreCase);
        s = ConvertPowers(s);
        return s;
    }

    private static string ConvertPowers(string input)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '^' && i < input.Length - 1)
            {
                sb.Append("^{");
                i++;
                if (input[i] == '(')
                {
                    var depth = 1;
                    i++;
                    while (i < input.Length && depth > 0)
                    {
                        if (input[i] == '(') depth++;
                        else if (input[i] == ')') depth--;

                        if (depth > 0)
                            sb.Append(input[i]);
                        i++;
                    }
                    sb.Append('}');
                    i--;
                }
                else
                {
                    sb.Append(input[i]);
                    sb.Append('}');
                }
            }
            else
            {
                sb.Append(input[i]);
            }
        }
        return sb.ToString();
    }

    private enum TokenType
    {
        Number,
        Identifier,
        Plus,
        Minus,
        Multiply,
        Divide,
        Power,
        LeftParen,
        RightParen,
        Comma,
        End
    }

    private readonly record struct Token(TokenType Type, string Text);

    private interface INode
    {
        Complex Evaluate(double t);
    }

    private sealed class NumberNode : INode
    {
        private readonly Complex _value;
        public NumberNode(double value) => _value = new Complex(value, 0);
        public Complex Evaluate(double t) => _value;
    }

    private sealed class VariableNode : INode
    {
        public Complex Evaluate(double t) => new(t, 0);
    }

    private sealed class ConstantNode : INode
    {
        private readonly Complex _value;
        public ConstantNode(Complex value) => _value = value;
        public Complex Evaluate(double t) => _value;
    }

    private sealed class UnaryNode : INode
    {
        private readonly TokenType _op;
        private readonly INode _operand;

        public UnaryNode(TokenType op, INode operand)
        {
            _op = op;
            _operand = operand;
        }

        public Complex Evaluate(double t)
        {
            var value = _operand.Evaluate(t);
            return _op switch
            {
                TokenType.Plus => value,
                TokenType.Minus => -value,
                _ => throw new InvalidOperationException("Ungültiger unärer Operator.")
            };
        }
    }

    private sealed class BinaryNode : INode
    {
        private readonly TokenType _op;
        private readonly INode _left;
        private readonly INode _right;

        public BinaryNode(INode left, TokenType op, INode right)
        {
            _left = left;
            _op = op;
            _right = right;
        }

        public Complex Evaluate(double t)
        {
            var l = _left.Evaluate(t);
            var r = _right.Evaluate(t);
            return _op switch
            {
                TokenType.Plus => l + r,
                TokenType.Minus => l - r,
                TokenType.Multiply => l * r,
                TokenType.Divide => l / r,
                TokenType.Power => Complex.Pow(l, r),
                _ => throw new InvalidOperationException("Ungültiger binärer Operator.")
            };
        }
    }

    private sealed class FunctionNode : INode
    {
        private readonly string _name;
        private readonly IReadOnlyList<INode> _args;

        public FunctionNode(string name, IReadOnlyList<INode> args)
        {
            _name = name.ToLowerInvariant();
            _args = args;
        }

        public Complex Evaluate(double t)
        {
            var values = _args.Select(a => a.Evaluate(t)).ToArray();
            return _name switch
            {
                "sin" => CheckArgCount(values, 1, v => Complex.Sin(v[0])),
                "cos" => CheckArgCount(values, 1, v => Complex.Cos(v[0])),
                "tan" => CheckArgCount(values, 1, v => Complex.Tan(v[0])),
                "asin" => CheckArgCount(values, 1, v => Complex.Asin(v[0])),
                "acos" => CheckArgCount(values, 1, v => Complex.Acos(v[0])),
                "atan" => CheckArgCount(values, 1, v => Complex.Atan(v[0])),
                "sinh" => CheckArgCount(values, 1, v => Complex.Sinh(v[0])),
                "cosh" => CheckArgCount(values, 1, v => Complex.Cosh(v[0])),
                "tanh" => CheckArgCount(values, 1, v => Complex.Tanh(v[0])),
                "exp" => CheckArgCount(values, 1, v => Complex.Exp(v[0])),
                "log" => CheckArgCount(values, 1, v => Complex.Log(v[0])),
                "log10" => CheckArgCount(values, 1, v => Complex.Log10(v[0])),
                "sqrt" => CheckArgCount(values, 1, v => Complex.Sqrt(v[0])),
                "abs" => CheckArgCount(values, 1, v => new Complex(v[0].Magnitude, 0)),
                "mag" => CheckArgCount(values, 1, v => new Complex(v[0].Magnitude, 0)),
                "phase" => CheckArgCount(values, 1, v => new Complex(v[0].Phase, 0)),
                "real" => CheckArgCount(values, 1, v => new Complex(v[0].Real, 0)),
                "imag" => CheckArgCount(values, 1, v => new Complex(v[0].Imaginary, 0)),
                "re" => CheckArgCount(values, 1, v => new Complex(v[0].Real, 0)),
                "im" => CheckArgCount(values, 1, v => new Complex(v[0].Imaginary, 0)),
                "conj" => CheckArgCount(values, 1, v => Complex.Conjugate(v[0])),
                "min" => CheckArgCount(values, 2, v => new Complex(Math.Min(v[0].Real, v[1].Real), 0)),
                "max" => CheckArgCount(values, 2, v => new Complex(Math.Max(v[0].Real, v[1].Real), 0)),
                "pow" => CheckArgCount(values, 2, v => Complex.Pow(v[0], v[1])),
                "cis" => CheckArgCount(values, 1, v => Complex.FromPolarCoordinates(1.0, v[0].Real)),
                _ => throw new InvalidOperationException($"Unbekannte Funktion '{_name}'.")
            };
        }

        private static Complex CheckArgCount(Complex[] values, int expected, Func<Complex[], Complex> eval)
        {
            if (values.Length != expected)
                throw new InvalidOperationException($"Funktion erwartet {expected} Argument(e), erhalten: {values.Length}.");
            return eval(values);
        }
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _index;

        public Parser(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("Die Formel ist leer.");

            _tokens = Tokenize(expression);
        }

        public INode Parse()
        {
            var node = ParseExpression();
            Expect(TokenType.End);
            return node;
        }

        private INode ParseExpression()
        {
            var left = ParseTerm();
            while (Match(TokenType.Plus) || Match(TokenType.Minus))
            {
                var op = Previous();
                var right = ParseTerm();
                left = new BinaryNode(left, op.Type, right);
            }
            return left;
        }

        private INode ParseTerm()
        {
            var left = ParsePower();
            while (Match(TokenType.Multiply) || Match(TokenType.Divide))
            {
                var op = Previous();
                var right = ParsePower();
                left = new BinaryNode(left, op.Type, right);
            }
            return left;
        }

        private INode ParsePower()
        {
            var left = ParseUnary();
            if (Match(TokenType.Power))
            {
                var op = Previous();
                var right = ParsePower();
                return new BinaryNode(left, op.Type, right);
            }
            return left;
        }

        private INode ParseUnary()
        {
            if (Match(TokenType.Plus) || Match(TokenType.Minus))
                return new UnaryNode(Previous().Type, ParseUnary());

            return ParsePrimary();
        }

        private INode ParsePrimary()
        {
            if (Match(TokenType.Number))
            {
                var value = double.Parse(Previous().Text, CultureInfo.InvariantCulture);
                return new NumberNode(value);
            }

            if (Match(TokenType.Identifier))
            {
                var name = Previous().Text;
                if (Match(TokenType.LeftParen))
                {
                    var args = new List<INode>();
                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            args.Add(ParseExpression());
                        }
                        while (Match(TokenType.Comma));
                    }
                    Expect(TokenType.RightParen);
                    return new FunctionNode(name, args);
                }

                return name.ToLowerInvariant() switch
                {
                    "t" => new VariableNode(),
                    "pi" => new ConstantNode(new Complex(Math.PI, 0)),
                    "e" => new ConstantNode(new Complex(Math.E, 0)),
                    "i" => new ConstantNode(Complex.ImaginaryOne),
                    _ => throw new InvalidOperationException($"Unbekannte Konstante oder Variable '{name}'.")
                };
            }

            if (Match(TokenType.LeftParen))
            {
                var node = ParseExpression();
                Expect(TokenType.RightParen);
                return node;
            }

            throw new InvalidOperationException($"Unerwartetes Token '{Peek().Text}'.");
        }

        private bool Match(TokenType type)
        {
            if (!Check(type)) return false;
            _index++;
            return true;
        }

        private void Expect(TokenType type)
        {
            if (!Match(type))
                throw new InvalidOperationException($"Erwartet: {type}, gefunden: {Peek().Text}");
        }

        private bool Check(TokenType type) => Peek().Type == type;
        private Token Peek() => _tokens[_index];
        private Token Previous() => _tokens[_index - 1];

        private static List<Token> Tokenize(string expression)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < expression.Length)
            {
                var c = expression[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsDigit(c) || c == '.')
                {
                    var start = i;
                    i++;
                    while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                        i++;

                    if (i < expression.Length && (expression[i] == 'e' || expression[i] == 'E'))
                    {
                        i++;
                        if (i < expression.Length && (expression[i] == '+' || expression[i] == '-'))
                            i++;
                        while (i < expression.Length && char.IsDigit(expression[i]))
                            i++;
                    }

                    tokens.Add(new Token(TokenType.Number, expression[start..i]));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    i++;
                    while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                        i++;
                    tokens.Add(new Token(TokenType.Identifier, expression[start..i]));
                    continue;
                }

                tokens.Add(c switch
                {
                    '+' => new Token(TokenType.Plus, "+"),
                    '-' => new Token(TokenType.Minus, "-"),
                    '*' => new Token(TokenType.Multiply, "*"),
                    '/' => new Token(TokenType.Divide, "/"),
                    '^' => new Token(TokenType.Power, "^"),
                    '(' => new Token(TokenType.LeftParen, "("),
                    ')' => new Token(TokenType.RightParen, ")"),
                    ',' => new Token(TokenType.Comma, ","),
                    _ => throw new InvalidOperationException($"Ungültiges Zeichen '{c}'.")
                });
                i++;
            }

            tokens.Add(new Token(TokenType.End, string.Empty));
            return tokens;
        }
    }
}
