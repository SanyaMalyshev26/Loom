namespace Loom.Models;

public static class ProductArtwork
{
    public static string? GetIllustration(Product product)
    {
        if (!UsesPlaceholder(product.ImageUrl))
            return null;

        return product.Name.Trim().ToLowerInvariant() switch
        {
            "джинсы" => "~/images/garments/jeans.svg",
            "куртка" => "~/images/garments/jacket.svg",
            "футболка" => "~/images/garments/tee.svg",
            "свитер" => "~/images/garments/sweater.svg",
            _ => null
        };
    }

    public static bool UsesPlaceholder(string? imageUrl) =>
        string.IsNullOrWhiteSpace(imageUrl) ||
        imageUrl.StartsWith("https://placehold.co/", StringComparison.OrdinalIgnoreCase);
}
