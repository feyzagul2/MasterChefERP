using System;

namespace MasterChefTakip.Services;

public sealed record MCProductionPalletValidationResult(
    long AvailableUnits,
    long RequestedUnits,
    long RemainingUnits,
    long FullBoxes,
    int LooseUnits,
    decimal RequestedKg,
    decimal RemainingKg
);

public static class MCProductionPalletValidation
{
    public static MCProductionPalletValidationResult Validate(
        decimal producedKg,
        decimal unitGrams,
        int unitsPerBox,
        long alreadyAllocatedUnits,
        int requestedBoxes,
        int requestedLooseUnits,
        long receiptCardId,
        long palletProductId,
        bool productMappingVerified)
    {
        if (!productMappingVerified)
            throw new InvalidOperationException(
                "Uretim karti ile palet urun karti eslesmesi " +
                "dogrulanmadan aktarim yapilamaz.");

        if (receiptCardId <= 0 || palletProductId <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(receiptCardId),
                "Gecerli urun kartlari gereklidir.");

        if (alreadyAllocatedUnits < 0)
            throw new ArgumentOutOfRangeException(
                nameof(alreadyAllocatedUnits));

        if (requestedBoxes < 0 || requestedLooseUnits < 0)
            throw new ArgumentOutOfRangeException(
                nameof(requestedBoxes),
                "Negatif aktarim miktari kabul edilmez.");

        var packaging = MCProductionPackagingCalculator.Calculate(
            producedKg,
            unitGrams,
            unitsPerBox);

        if (alreadyAllocatedUnits > packaging.TotalUnits)
            throw new InvalidOperationException(
                "Onceden aktarilan miktar uretimi asiyor.");

        long requestedUnits = checked(
            (long)requestedBoxes * unitsPerBox +
            requestedLooseUnits);

        if (requestedUnits == 0)
            throw new InvalidOperationException(
                "Sifir miktarli palet aktarimi yapilamaz.");

        long availableUnits = checked(
            packaging.TotalUnits - alreadyAllocatedUnits);

        if (requestedUnits > availableUnits)
            throw new InvalidOperationException(
                "Palete aktarilmak istenen miktar " +
                "kalan mamul miktarini asiyor.");

        long remainingUnits = availableUnits - requestedUnits;

        return new MCProductionPalletValidationResult(
            availableUnits,
            requestedUnits,
            remainingUnits,
            requestedUnits / unitsPerBox,
            (int)(requestedUnits % unitsPerBox),
            requestedUnits * unitGrams / 1000m,
            remainingUnits * unitGrams / 1000m
        );
    }
}