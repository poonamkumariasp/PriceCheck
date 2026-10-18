# PriceCheck project guidance

- Keep the interface responsive and accessible across phone, tablet, and desktop widths.
- The application uses ASP.NET Core Razor Pages, EF Core, and SQLite.
- Store-page price import reads supported Schema.org Product/Offer JSON-LD; keep manual entry as fallback and do not imply every retailer page is supported.
- Retailer-list import reads supported structured ItemList/Product data and is capped at 100 products per page; keep category assignment explicit.
- Save history only when price or currency changes, and retain observations for 12 months.
- Keep product and price-history behavior scoped to the existing `Product` and `PriceObservation` domain.
- The web application lives in `PriceCheck.App`; tests live in `PriceCheck.Tests` at the repository root.
- Run `dotnet build PriceCheck.App/PriceCheck.csproj` after application changes.