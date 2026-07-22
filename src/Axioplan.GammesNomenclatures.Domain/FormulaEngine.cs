using System.Globalization;
using System.Text;

namespace Axioplan.GammesNomenclatures.Domain;

public static class FormulaEngine
{
    public const string BesoinBaseToken = "BesoinBase";

    private static readonly HashSet<string> AllowedOperators = ["+", "-", "*", "/"];

    public static string NormalizeExpression(string expression)
        => string.Join(' ', Tokenize(expression));

    public static IReadOnlyList<string> Tokenize(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in expression)
        {
            if (char.IsWhiteSpace(ch))
            {
                FlushCurrent();
                continue;
            }

            if (ch is '(' or ')')
            {
                FlushCurrent();
                tokens.Add(ch.ToString());
                continue;
            }

            if (AllowedOperators.Contains(ch.ToString()))
            {
                FlushCurrent();
                tokens.Add(ch.ToString());
                continue;
            }

            current.Append(ch);
        }

        FlushCurrent();
        return tokens;

        void FlushCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    public static void Validate(string expression, IReadOnlySet<string> allowedArguments)
    {
        var tokens = Tokenize(expression);
        if (tokens.Count == 0)
        {
            throw new InvalidOperationException("La formule est vide.");
        }

        var allowed = new HashSet<string>(allowedArguments, StringComparer.OrdinalIgnoreCase)
        {
            BesoinBaseToken
        };

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (AllowedOperators.Contains(token) || token is "(" or ")")
            {
                continue;
            }

            if (!allowed.Contains(token))
            {
                throw new InvalidOperationException($"Token non autorise dans la formule : '{token}'.");
            }
        }

        _ = Evaluate(expression, 1, allowedArguments.ToDictionary(
            argument => argument,
            _ => 1.0,
            StringComparer.OrdinalIgnoreCase));
    }

    public static double Evaluate(
        string expression,
        double besoinBase,
        IReadOnlyDictionary<string, double> argumentCoefficients)
    {
        var tokens = Tokenize(expression);
        if (tokens.Count == 0)
        {
            throw new InvalidOperationException("La formule est vide.");
        }

        var operands = new Stack<double>();
        var operators = new Stack<char>();

        foreach (var token in tokens)
        {
            if (token == "(")
            {
                operators.Push('(');
                continue;
            }

            if (token == ")")
            {
                while (operators.Count > 0 && operators.Peek() != '(')
                {
                    ApplyOperator(operators.Pop(), operands);
                }

                if (operators.Count == 0 || operators.Pop() != '(')
                {
                    throw new InvalidOperationException("Parentheses invalides dans la formule.");
                }

                continue;
            }

            if (AllowedOperators.Contains(token))
            {
                var currentOperator = token[0];
                while (operators.Count > 0
                       && operators.Peek() != '('
                       && Precedence(operators.Peek()) >= Precedence(currentOperator))
                {
                    ApplyOperator(operators.Pop(), operands);
                }

                operators.Push(currentOperator);
                continue;
            }

            operands.Push(ResolveTokenValue(token, besoinBase, argumentCoefficients));
        }

        while (operators.Count > 0)
        {
            if (operators.Peek() == '(')
            {
                throw new InvalidOperationException("Parentheses invalides dans la formule.");
            }

            ApplyOperator(operators.Pop(), operands);
        }

        if (operands.Count != 1)
        {
            throw new InvalidOperationException("Formule invalide : expression mal formee.");
        }

        return operands.Pop();
    }

    public static string BuildDisplayExpression(IReadOnlyList<string> tokens)
        => string.Join(' ', tokens);

    private static double ResolveTokenValue(
        string token,
        double besoinBase,
        IReadOnlyDictionary<string, double> argumentCoefficients)
    {
        if (string.Equals(token, BesoinBaseToken, StringComparison.OrdinalIgnoreCase))
        {
            return besoinBase;
        }

        if (AllowedOperators.Contains(token) || token is "(" or ")")
        {
            return 0;
        }

        if (argumentCoefficients.TryGetValue(token, out var coefficient))
        {
            return coefficient;
        }

        foreach (var pair in argumentCoefficients)
        {
            if (string.Equals(pair.Key, token, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return 1.0;
    }

    private static int Precedence(char operatorSymbol)
        => operatorSymbol is '*' or '/' ? 2 : 1;

    private static void ApplyOperator(char operatorSymbol, Stack<double> operands)
    {
        if (operands.Count < 2)
        {
            throw new InvalidOperationException("Formule invalide : operateur sans operande suffisant.");
        }

        var right = operands.Pop();
        var left = operands.Pop();
        var result = operatorSymbol switch
        {
            '+' => left + right,
            '-' => left - right,
            '*' => left * right,
            '/' => right == 0
                ? throw new DivideByZeroException("Division par zero dans la formule.")
                : left / right,
            _ => throw new InvalidOperationException($"Operateur non autorise : {operatorSymbol}")
        };

        if (double.IsNaN(result) || double.IsInfinity(result))
        {
            throw new InvalidOperationException("Resultat de formule non valide.");
        }

        operands.Push(result);
    }
}
