using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace GhostCursorSharp
{
    public class Sample
    {
        public async Task Execute()
        {
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();
            await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = false, DefaultViewport = null });
            await using var page = await browser.NewPageAsync();

            var ghostCursor = new GhostCursor();

            await page.GoToAsync("https://www.amazon.com/dp/B00BM48FB4");

            var button = await page.QuerySelectorAsync("#add-to-cart-button");


            var cursor = await ghostCursor.CreateCursor(page, GhostMath.Origin);
            await MouseHelper.InstallMouseHelper(page);

            /*var times = 50;
            var random = new Random();
            while(times > 0)
            {
                times--;
                await cursor.MoveTo(new System.Numerics.Vector2(random.Next(0, 400), random.Next(0, 400)));
                await Task.Delay(800);
            }*/

            await cursor.Click(button, null);
        }
    }
}
