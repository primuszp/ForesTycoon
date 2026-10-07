namespace DendroKit.Core.Params;

/// <summary>
/// Generation-safe numeric limits layered on top of Arbaro's declared
/// parameter storage limits. These rules are shared by validation and the UI.
/// </summary>
public static class ParameterLimits
{
    public readonly record struct NumericRange(double Min, double Max);

    public static NumericRange GetEffectiveRange(
        AbstractParam param,
        IReadOnlyDictionary<string, AbstractParam>? parameters = null)
    {
        double min;
        double max;

        if (param is IntParam ip)
        {
            min = ip.Min;
            max = ip.Max;
        }
        else if (param is FloatParam fp)
        {
            min = fp.Min;
            max = fp.Max;
        }
        else
        {
            return new NumericRange(0, 1);
        }

        switch (param.Name)
        {
            case "Levels":
                min = Math.Max(min, 1);
                break;

            case "Leaves":
                min = Math.Max(min, (double)int.MinValue + 1);
                break;

            case "BaseSize" when GetInt(parameters, "Levels") > 1:
                max = Math.Min(max, Math.BitDecrement(1.0));
                break;

            case "PruneWidthPeak" when EnvelopeIsActive(parameters):
                max = Math.Min(max, Math.BitDecrement(1.0));
                break;

            default:
                if (param.Name.EndsWith("SplitAngle", StringComparison.Ordinal)
                    && SplitIsActive(param.Level, parameters))
                {
                    min = Math.Max(min, double.Epsilon);
                }
                break;
        }

        return new NumericRange(min, max);
    }

    public static void ValidateForGeneration(
        IReadOnlyDictionary<string, AbstractParam> parameters)
    {
        foreach (AbstractParam param in parameters.Values)
        {
            if (param is not (IntParam or FloatParam))
                continue;

            NumericRange range = GetEffectiveRange(param, parameters);
            double value = param is IntParam ip ? ip.IntValue() : ((FloatParam)param).DoubleValue();

            if (value < range.Min || value > range.Max)
            {
                throw new ParamException(
                    $"Parameter {param.Name}={param.GetValue()} is outside its "
                    + $"generation-safe range [{Format(range.Min)}, {Format(range.Max)}].");
            }
        }
    }

    private static bool EnvelopeIsActive(IReadOnlyDictionary<string, AbstractParam>? parameters) =>
        GetInt(parameters, "Shape") == TreeParams.Envelope
        || GetDouble(parameters, "PruneRatio") > 0;

    private static bool SplitIsActive(
        int level,
        IReadOnlyDictionary<string, AbstractParam>? parameters) =>
        level >= 0 && GetDouble(parameters, $"{level}SegSplits") > 0;

    private static int GetInt(
        IReadOnlyDictionary<string, AbstractParam>? parameters,
        string name) =>
        parameters != null
        && parameters.TryGetValue(name, out AbstractParam? p)
        && p is IntParam ip
            ? ip.IntValue()
            : 0;

    private static double GetDouble(
        IReadOnlyDictionary<string, AbstractParam>? parameters,
        string name) =>
        parameters != null
        && parameters.TryGetValue(name, out AbstractParam? p)
        && p is FloatParam fp
            ? fp.DoubleValue()
            : 0;

    private static string Format(double value) =>
        value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
