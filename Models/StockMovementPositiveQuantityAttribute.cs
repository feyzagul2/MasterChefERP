using System;
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models
{
    // MC_STOCK_POSITIVE_QUANTITY_V1

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class StockMovementPositiveQuantityAttribute
        : ValidationAttribute
    {
        protected override ValidationResult? IsValid(
            object? value,
            ValidationContext validationContext)
        {
            if (value is not StockMovement movement)
                return ValidationResult.Success;

            if (movement.BoxCount > 0 ||
                movement.LooseUnitCount > 0)
                return ValidationResult.Success;

            return new ValidationResult(
                "Stok hareket miktari sifirdan buyuk olmalidir.",
                new[]
                {
                    nameof(StockMovement.BoxCount),
                    nameof(StockMovement.LooseUnitCount)
                });
        }
    }
}