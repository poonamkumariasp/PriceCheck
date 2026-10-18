# PriceCheck

PriceCheck is a responsive price-tracking web app built with ASP.NET Core Razor Pages, EF Core, and SQLite. It is designed to work in a phone, tablet, or desktop browser.

## Requirements

- .NET 10 SDK

## Run locally

```powershell
dotnet restore
dotnet run
```

Open the local URL printed by `dotnet run`. On first launch the app creates `pricecheck.db` and seeds a few sample products so the dashboard is populated. Add products and enter their prices manually; automated retailer price collection is not included in this starter.

## Data model

- `Product`: name and optional retailer product URL.
- `PriceObservation`: retailer, amount, currency, and observation time.

SQLite stores data in the project directory during local development. The database file is ignored by Git.

## GitHub

Create an empty repository named `PriceCheck` in your GitHub account, then connect and push this local repository:

```powershell
git remote add origin https://github.com/<your-account>/PriceCheck.git
git push -u origin main
```

The GitHub Actions workflow restores, builds, and tests the project on pushes and pull requests.