using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GhostCursorSharp
{
    public class MouseHelper
    {
        public static async Task InstallMouseHelper(Page page)
        {
            var path = Path.Combine(Environment.CurrentDirectory, "GhostCursor", "follow-cursor.js"); 
            string jsContent = await File.ReadAllTextAsync(path);
            await page.EvaluateExpressionHandleAsync(jsContent);
        }
    }
}
