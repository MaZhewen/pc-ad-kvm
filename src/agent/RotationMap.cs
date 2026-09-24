using System;

namespace PcKvm
{
    /// <summary>Keep the cursor on the same physical part of the phone when its display rotates.</summary>
    public static class RotationMap
    {
        public static void Map(int x, int y, int oldW, int oldH, int oldRotation,
                               int newW, int newH, int newRotation, out int newX, out int newY)
        {
            if (oldW <= 0 || oldH <= 0 || newW <= 0 || newH <= 0)
                throw new ArgumentOutOfRangeException("display size");

            int naturalX, naturalY;
            switch (oldRotation)
            {
                case 90: naturalX = y; naturalY = oldW - 1 - x; break;
                case 180: naturalX = oldW - 1 - x; naturalY = oldH - 1 - y; break;
                case 270: naturalX = oldH - 1 - y; naturalY = x; break;
                default: naturalX = x; naturalY = y; break;
            }

            int oldNaturalW = (oldRotation == 90 || oldRotation == 270) ? oldH : oldW;
            int oldNaturalH = (oldRotation == 90 || oldRotation == 270) ? oldW : oldH;
            int newNaturalW = (newRotation == 90 || newRotation == 270) ? newH : newW;
            int newNaturalH = (newRotation == 90 || newRotation == 270) ? newW : newH;
            naturalX = Scale(naturalX, oldNaturalW, newNaturalW);
            naturalY = Scale(naturalY, oldNaturalH, newNaturalH);

            switch (newRotation)
            {
                case 90: newX = newW - 1 - naturalY; newY = naturalX; break;
                case 180: newX = newW - 1 - naturalX; newY = newH - 1 - naturalY; break;
                case 270: newX = naturalY; newY = newH - 1 - naturalX; break;
                default: newX = naturalX; newY = naturalY; break;
            }
            newX = Math.Max(0, Math.Min(newW - 1, newX));
            newY = Math.Max(0, Math.Min(newH - 1, newY));
        }

        static int Scale(int value, int oldSpan, int newSpan)
        {
            if (oldSpan <= 1) return 0;
            return (int)Math.Round((double)value * (newSpan - 1) / (oldSpan - 1));
        }
    }
}
