using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceCheck.Services;

public sealed record ImportedProduct(string Name, decimal Price, string Currency, string Url, string Retailer);
public sealed record ImportedCatalog(string Retailer, IReadOnlyList<ImportedProduct> Products);

public sealed class ProductImportException(string message) : Exception(message);

public sealed class ProductPagePriceImporter(HttpClient client)
{
    private const int MaximumCatalogProducts = 100;
    private static readonly Regex JsonLdScript = new(
        "<script\\b(?=[^>]*\\btype\\s*=\\s*['\\\"]application/ld\\+json['\\\"])[^>]*>(.*?)</script\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public async Task<ImportedProduct> ImportAsync(string productUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(productUrl, UriKind.Absolute, out var uri))
        {
            throw new ProductImportException("Enter a complete product page URL.");
        }

        var response = await GetSafeResponseAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        var product = ParseProduct(html);
        return product with
        {
            Url = finalUri.AbsoluteUri,
            Retailer = finalUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? finalUri.Host[4..]
                : finalUri.Host
        };
    }

    public async Task<ImportedCatalog> ImportCatalogAsync(string catalogUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(catalogUrl, UriKind.Absolute, out var uri))
        {
            throw new ProductImportException("Enter a complete retailer products-list URL.");
        }

        var response = await GetSafeResponseAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        var retailer = GetRetailerName(finalUri);
        var products = ParseCatalog(html, finalUri)
            .Where(product => Uri.TryCreate(product.Url, UriKind.Absolute, out _))
            .Take(MaximumCatalogProducts)
            .Select(product => product with { Retailer = retailer })
            .ToList();
        if (products.Count == 0)
        {
            throw new ProductImportException("This retailer page does not expose a supported structured product list with individual product URLs and prices.");
        }

        return new ImportedCatalog(retailer, products);
    }

    public static ImportedProduct ParseProduct(string html)
    {
        foreach (Match match in JsonLdScript.Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
                if (TryFindProduct(document.RootElement, out var product))
                {
                    return product;
                }
            }
            catch (JsonException)
            {
                // A page may contain unrelated or malformed structured-data blocks.
            }
        }

        throw new ProductImportException("This product page does not expose a supported structured price. Try another store page or enter the price manually.");
    }

    public static IReadOnlyList<ImportedProduct> ParseCatalog(string html, Uri catalogUri)
    {
        var products = new Dictionary<string, ImportedProduct>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in JsonLdScript.Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
                CollectProducts(document.RootElement, catalogUri, products);
            }
            catch (JsonException)
            {
                // Ignore malformed or unrelated structured-data blocks.
            }
        }

        return products.Values.Take(MaximumCatalogProducts).ToList();
    }

    private static void CollectProducts(JsonElement element, Uri catalogUri, IDictionary<string, ImportedProduct> products)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectProducts(item, catalogUri, products);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;

        if (HasProductType(element) && TryGetString(element, "name", out var name) &&
            element.TryGetProperty("offers", out var offers) && TryFindOffer(offers, out var price, out var currency))
        {
            var url = TryGetString(element, "url", out var productUrl) ? productUrl : string.Empty;
            if (url.Length == 0 && TryFindOfferUrl(offers, out var offerUrl)) url = offerUrl;
            if (url.Length > 0 && Uri.TryCreate(catalogUri, url, out var absoluteUrl) && absoluteUrl.Scheme is "http" or "https")
            {
                url = absoluteUrl.AbsoluteUri;
            }
            else if (url.Length > 0)
            {
                url = string.Empty;
            }

            var item = new ImportedProduct(name, price, currency, url, GetRetailerName(catalogUri));
            var key = url.Length > 0 ? url : $"{name}|{currency}";
            products.TryAdd(key, item);
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                CollectProducts(property.Value, catalogUri, products);
        }
    }

    private static bool TryFindOfferUrl(JsonElement element, out string url)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var offer in element.EnumerateArray())
            {
                if (TryFindOfferUrl(offer, out url)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetString(element, "url", out url)) return true;
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
                    TryFindOfferUrl(property.Value, out url)) return true;
            }
        }

        url = string.Empty;
        return false;
    }

    private static string GetRetailerName(Uri uri) => uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
        ? uri.Host[4..]
        : uri.Host;

    private static bool TryFindProduct(JsonElement element, out ImportedProduct product)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindProduct(item, out product)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (HasProductType(element) &&
                TryGetString(element, "name", out var name) &&
                element.TryGetProperty("offers", out var offers) &&
                TryFindOffer(offers, out var price, out var currency))
            {
                product = new ImportedProduct(name, price, currency, string.Empty, string.Empty);
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
                    TryFindProduct(property.Value, out product))
                {
                    return true;
                }
            }
        }

        product = null!;
        return false;
    }

    private static bool TryFindOffer(JsonElement element, out decimal price, out string currency)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var offer in element.EnumerateArray())
            {
                if (TryFindOffer(offer, out price, out currency)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetString(element, "price", out var rawPrice) &&
                decimal.TryParse(rawPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out price) &&
                price >= 0 && TryGetString(element, "priceCurrency", out currency) &&
                currency.Length == 3 && currency.All(char.IsAsciiLetter))
            {
                currency = currency.ToUpperInvariant();
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
                    TryFindOffer(property.Value, out price, out currency))
                {
                    return true;
                }
            }
        }

        price = 0;
        currency = string.Empty;
        return false;
    }

    private static bool HasProductType(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type)) return false;
        return type.ValueKind == JsonValueKind.String
            ? type.GetString() == "Product"
            : type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(item => item.GetString() == "Product");
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private async Task<HttpResponseMessage> GetSafeResponseAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var redirects = 0; redirects <= 4; redirects++)
        {
            await ValidatePublicHttpUrlAsync(uri, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("PriceCheck/1.0 (personal price history)");
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!IsRedirect(response.StatusCode)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirects == 4)
            {
                throw new ProductImportException("The store page redirected too many times.");
            }

            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }

        throw new ProductImportException("Could not reach the store product page.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task ValidatePublicHttpUrlAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length > 0 || uri.Port is not (80 or 443))
        {
            throw new ProductImportException("Only public HTTP or HTTPS product page URLs are supported.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (SocketException)
        {
            throw new ProductImportException("The store website could not be found.");
        }

        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
        {
            throw new ProductImportException("That URL does not resolve to a public website.");
        }
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] is 0 or 10 or 127 || bytes[0] == 169 && bytes[1] == 254 ||
                bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168;
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xfe) == 0xfc;
    }
}