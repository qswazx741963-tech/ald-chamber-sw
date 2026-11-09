namespace Ald.Comm;

/// <summary>原始暫存器值 ↔ 工程單位。倍率來自 Generated/Registers.g.cs。</summary>
public static class Scaling
{
    public static double ToEng(ushort raw, double scale) => raw * scale;
    public static double ToEngSigned(ushort raw, double scale) => (short)raw * scale;

    public static ushort ToRaw(double value, double scale, double min, double max)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(nameof(value), $"{value} 超出範圍 {min}–{max}");
        return checked((ushort)Math.Round(value / scale));
    }

    public static int ToInt32(ushort hi, ushort lo) => (hi << 16) | lo;
}
