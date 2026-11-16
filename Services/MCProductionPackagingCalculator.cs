using System;

namespace MasterChefTakip.Services;

public sealed record MCProductionPackagingResult(
    decimal ProducedKg,
    decimal UnitGrams,
    int UnitsPerBox,
    int BoxesPerPallet,
    long TotalUnits,
    long FullBoxes,
    int LooseUnits,
    long FullPallets,
    int RemainingBoxes,
    decimal RemainingGrams,
    decimal PackagedKg
);

public static class MCProductionPackagingCalculator
{
    public static MCProductionPackagingResult Calculate(
        decimal producedKg,
        decimal unitGrams,
        int unitsPerBox,
        int boxesPerPallet = 84)
    {
        if (producedKg < 0)
            throw new ArgumentOutOfRangeException(
                nameof(producedKg));

        if (unitGrams <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(unitGrams));

        if (unitsPerBox <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(unitsPerBox));

        if (boxesPerPallet <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(boxesPerPallet));

        decimal totalGrams = checked(producedKg * 1000m);

        decimal possibleUnits = decimal.Floor(
            totalGrams / unitGrams);

        if (possibleUnits > long.MaxValue)
            throw new OverflowException(
                "Uretim miktari desteklenen siniri asiyor.");

        long totalUnits = (long)possibleUnits;

        long fullBoxes = totalUnits / unitsPerBox;

        int looseUnits = (int)(
            totalUnits % unitsPerBox);

        long fullPallets = fullBoxes / boxesPerPallet;

        int remainingBoxes = (int)(
            fullBoxes % boxesPerPallet);

        decimal packagedGrams = checked(
            totalUnits * unitGrams);

        decimal remainingGrams =
            totalGrams - packagedGrams;

        return new MCProductionPackagingResult(
            producedKg,
            unitGrams,
            unitsPerBox,
            boxesPerPallet,
            totalUnits,
            fullBoxes,
            looseUnits,
            fullPallets,
            remainingBoxes,
            remainingGrams,
            packagedGrams / 1000m
        );
    }
}