# PriceCheck

PriceCheck is a responsive price-tracking web app built with ASP.NET Core Razor Pages, EF Core, and SQLite. It is designed to work in a phone, tablet, or desktop browser.

## Requirements

- .NET 10 SDK

## Run locally

```powershell
dotnet restore
dotnet run
```

Open the local URL printed by `dotnet run`. On first launch the app creates `pricecheck.db` and seeds a few sample products so the dashboard is populated.

Paste a store product page URL into **Track a product** to import its name and price from Schema.org `Product`/`Offer` JSON-LD. Retailer markup varies: if a page does not provide supported structured price data, use the manual entry form. Do not assume every store page can be imported; anti-bot controls, geographic restrictions, store selection, and retailer terms may affect availability. Aldi UK's example product page exposes its current price and currency in JSON-LD. Online prices can differ from in-store prices.

The first successful import establishes the tracking baseline; the app cannot reconstruct prices from before that date. While the app is running, tracked product pages are checked every 24 hours. A `PriceObservation` is stored only when the amount or currency changes, and observations older than 12 months are pruned. This is an app-hosted timer, not a separate cloud scheduler: checks pause whenever the app is stopped.

Run the parser and price-history tests with:

```powershell
dotnet test tests/PriceCheck.Tests/PriceCheck.Tests.csproj
```

## Data model

- `Product`: name and optional retailer product URL.
- `PriceObservation`: retailer, amount, currency, and observation time.

Each product history shows price changes observed during the retained 12-month window. Prices retain their retailer currency; the dashboard does not convert currencies.

SQLite stores data in the project directory during local development. The database file is ignored by Git.

## GitHub

Create an empty repository named `PriceCheck` in your GitHub account, then connect and push this local repository:

```powershell
git remote add origin https://github.com/<your-account>/PriceCheck.git
git push -u origin main
```

The GitHub Actions workflow restores, builds, and tests the project on pushes and pull requests.