using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace GhostCursorSharp
{
    public interface BoxOptions
    {
        public int PaddingPercentage { get; set; }
    }

    public interface MoveOptions : BoxOptions
    {
        public int WaitForSelector { get; set; }
        public int? MoveDelay { get; set; }
    }

    public interface ClickOptions : MoveOptions
    {
        public int WaitForClick { get; set; }
    }

    public class GhostCursorActions
    {
        public Action<bool> ToggleRandomMove { get; set; } //  (random: boolean) => void
        public Func<ElementHandle, ClickOptions, Task> Click { get; set; } //: (selector?: string | ElementHandle, options?: ClickOptions) => Promise<void>
        public Func<ElementHandle, MoveOptions, Task> Move { get; set; } //: (selector: string | ElementHandle, options?: MoveOptions) => Promise<void>
        public Func<Vector2, Task> MoveTo { get; set; }  //: (destination: Vector2) => Promise<void>
    }

    public class GhostCursor
    {        
        private Random Randomizer = new Random();
     
        /// <summary>
        /// Calculate the amount of time needed to move from (x1, y1) to (x2, y2)
        /// given the width of the element being clicked on
        /// https://en.wikipedia.org/wiki/Fitts%27s_law
        /// </summary>
        private double Fitts(double distance, double width)
        {
            var a = 0;
            var b = 2;
            var id = Math.Log(distance / width + 1, 2);

            return a + b * id;
        }

        private static double OvershootThreshold = 500;
        private Func<Vector2, Vector2, bool> ShouldOvershoot = (Vector2 a, Vector2 b) => GhostMath.Magnitude(GhostMath.Direction(a, b)) > OvershootThreshold;
        
        private Vector2 GetRandomBoxPoint(BoundingBox box, BoxOptions? options = null)
        {
            // { x, y, width, height

            decimal paddingWidth = 0;
            decimal paddingHeight = 0;

            if (options?.PaddingPercentage != null && options?.PaddingPercentage > 0 && options?.PaddingPercentage < 100)
            {
                paddingWidth = box.Width * options.PaddingPercentage / 100;
                paddingHeight = box.Height * options.PaddingPercentage / 100;
            }

            return new Vector2
            {
                X = Convert.ToSingle(box.X + (paddingWidth / 2) + Convert.ToDecimal(Randomizer.NextDouble()) * (box.Width - paddingWidth)),
                Y = Convert.ToSingle(box.Y + (paddingHeight / 2) + Convert.ToDecimal(Randomizer.NextDouble()) * (box.Height - paddingHeight))
            };
        }

        // Get a random point on a browser window
        public async Task<Vector2> GetRandomPagePoint(Page page)
        {
            string targetId = page.Target.TargetId;
            dynamic window = await page.Client.SendAsync("Browser.getWindowForTarget", new { targetId });

            var width = Convert.ToDecimal(window.bounds.width);
            var height = Convert.ToDecimal(window.bounds.height);

            return GetRandomBoxPoint(new BoundingBox { X = Convert.ToDecimal(GhostMath.Origin.X), Y = Convert.ToDecimal(GhostMath.Origin.Y), Width = width, Height = height });
        }

        // Using this method to get correct position of Inline elements (elements like <a>)
        private async Task<BoundingBox?> GetElementBox(Page page, ElementHandle element, bool relativeToMainFrame = true)
        {
            if (element.RemoteObject.ObjectId == null)
            {
                return null;
            }

            dynamic quads;
            try
            {
                quads = await page.Client.SendAsync("DOM.getContentQuads", new { objectId = element.RemoteObject.ObjectId });
            }
            catch
            {
                // console.debug('Quads not found, trying regular boundingBox')
                return await element.BoundingBoxAsync();
            }

            var elementBox = new BoundingBox
            {
                X = quads.quads[0][0],
                Y = quads.quads[0][1],
                Width = quads.quads[0][4] - quads.quads[0][0],
                Height = quads.quads[0][5] - quads.quads[0][1]
            };

            if (elementBox == null)
            {
                return null;
            }

            if (!relativeToMainFrame)
            {
                Frame elementFrame = element.ExecutionContext.Frame;

                var iframes = await elementFrame.ParentFrame.XPathAsync("//iframe");

                ElementHandle? frame = null;
                if (iframes != null)
                {
                    foreach (var iframe in iframes)
                    {
                        if ((await iframe.ContentFrameAsync()) == elementFrame)
                            frame = iframe;
                    }
                }

                if (frame != null)
                {
                    var boundingBox = await frame.BoundingBoxAsync();
                    elementBox.X = boundingBox != null ? elementBox.X - boundingBox.X : elementBox.X;
                    elementBox.Y = boundingBox != null ? elementBox.Y - boundingBox.Y : elementBox.Y;
                }
            }

            return elementBox;
        }

        public List<Vector2> Path(Vector2 start, BoundingBox end, double? spreadOverride = null)
        {
            // var defaultWidth = 100;
            var minSteps = 25;
            var width = end.Width; // : defaultWidth; (if Vector2)
            var curve = GhostMath.BezierCurve(start, new Vector2(Convert.ToSingle(end.X), Convert.ToSingle(end.Y)), spreadOverride);
            var length = curve.Length * 0.8;
            var baseTime = Randomizer.NextDouble() * minSteps;
            var steps = Math.Ceiling((Math.Log(Fitts(length, Convert.ToDouble(width)) + 1, 2) + baseTime) * 3);

            // what is the equivalent of 
            var re = curve.Reduce(steps / 1000);
            List<Vector2> lookupTable = re.SelectMany(r => r.Points).ToList();

            return ClampPositive(lookupTable);
        }

        public List<Vector2> ClampPositive(List<Vector2> vectors)
        {
            Func<float, float> clamp0 = (float elem) => Math.Max(0, elem);

            return vectors.Select(vector =>
            {
                return new Vector2 { X = clamp0(vector.X), Y = clamp0(vector.Y) };
            }).ToList();
        }

        private BoundingBox VectorToBoundingboxDefaultWidth(Vector2 vector)
        {
            return new BoundingBox
            {
                X = Convert.ToDecimal(vector.X),
                Y = Convert.ToDecimal(vector.X),
                Width = 100
            };
        }

        private bool Moving = false;

        #region CreateCursor Helpers
        /// <summary>
        /// Move the mouse over a number of vectors
        /// </summary>
        private async Task<Vector2> tracePath(List<Vector2> vectors, bool abortOnMove, Page page, Vector2 previous)
        {
            foreach (var v in vectors)
            {
                try
                {
                    // In case this is called from random mouse movements and the users wants to move the mouse, abort
                    if (abortOnMove && this.Moving)
                    {
                        return previous;
                    }

                    await page.Mouse.MoveAsync(Convert.ToDecimal(v.X), Convert.ToDecimal(v.Y));
                    previous = v;
                }
                catch (Exception ex)
                {
                    // Exit function if the browser is no longer connected
                    if (!page.Browser.IsConnected)
                        return previous;

                    // console.debug('Warning: could not move mouse, error message:', ex.Message)
                }
            }

            return previous;
        }

        // Start random mouse movements. Function recursively calls itself
        private async Task<Vector2> randomMove(Page page, Vector2 previous, MoveOptions? options = null)
        {
            try
            {
                if (!this.Moving)
                {
                    var rand = await GetRandomPagePoint(page);
                    previous = await tracePath(Path(previous, VectorToBoundingboxDefaultWidth(rand)), true, page, previous);
                    previous = rand;
                }

                if (options?.MoveDelay != null && options.MoveDelay >= 0)
                {
                    await Task.Delay(Convert.ToInt32(Randomizer.NextDouble() * options.MoveDelay));
                }
                else
                {
                    await Task.Delay(Convert.ToInt32(Randomizer.NextDouble() * 2000));// 2s by default
                }

                previous = await randomMove(page, previous); // fire and forget, recursive function
            }
            catch
            {
                // console.debug('Warning: stopping random mouse movements')
            }

            return previous;
        }
        #endregion

        public async Task<GhostCursorActions> CreateCursor(Page page, Vector2 start, bool performRandomMoves = false)
        {
            // this is kind of arbitrary, not a big fan but it seems to work
            var overshootSpread = 10;
            var overshootRadius = 120;
            Vector2 previous = start;

            // Initial state: mouse is not moving
            //bool moving = false;    
            Action<bool> toggleRandomMove = random => { this.Moving = !random; };

            Func<ElementHandle, MoveOptions, Task> move = async (ElementHandle selector, MoveOptions options) =>
            {
                toggleRandomMove(false);
                ElementHandle elem = selector;

                // Make sure the object is in view
                if (elem.RemoteObject?.ObjectId != null)
                {
                    try
                    {
                        await page.Client.SendAsync("DOM.scrollIntoViewIfNeeded", new { objectId = elem.RemoteObject.ObjectId });
                    }
                    catch
                    {
                        // use regular JS scroll method as a fallback
                        await elem.EvaluateFunctionAsync("e => e.scrollIntoView({ behavior: 'smooth' })");
                    }
                }
                var box = await GetElementBox(page, elem);
                /*if (box == null)
                {
                        // not working on puppeterr sharp ?
                    box = await elem.EvaluateFunctionAsync("e => e.getBoundingClientRect()");
                }*/

                var destination = GetRandomBoxPoint(box, options);
                var dimensions = new { box.Height, box.Width };
                var overshooting = ShouldOvershoot(previous, destination);
                var to = overshooting ? GhostMath.Overshoot(destination, overshootRadius) : destination;
                previous = await tracePath(Path(previous, VectorToBoundingboxDefaultWidth(to)), false, page, previous);

                if (overshooting)
                {
                    var correction = Path(to, new BoundingBox { Height = box.Height, Width = box.Width, X = Convert.ToDecimal(destination.X), Y = Convert.ToDecimal(destination.Y) }, overshootSpread);

                    previous = await tracePath(correction, false, page, previous);
                }
                previous = destination;

                toggleRandomMove(true);
            };

            GhostCursorActions actions = new GhostCursorActions
            {
                ToggleRandomMove = (random) => { this.Moving = !random; },
                Click = async (selector, options) =>
                {
                    toggleRandomMove(false);

                    if (selector != null)
                    {
                        await move(selector, options);
                        toggleRandomMove(false);
                    }

                    try
                    {
                        await page.Mouse.DownAsync();
                        if (options?.WaitForClick != null)
                        {
                            await Task.Delay(options.WaitForClick);
                        }
                        await page.Mouse.UpAsync();
                    }
                    catch (Exception error)
                    {
                        // console.debug('Warning: could not click mouse, error message:', error)
                    }

                    if (options?.MoveDelay != null && options.MoveDelay >= 0)
                    {
                        await Task.Delay(Convert.ToInt32(Randomizer.NextDouble() * options.MoveDelay));
                    }
                    else
                    {
                        await Task.Delay(Convert.ToInt32(Randomizer.NextDouble() * 2000)); // 2s by default
                    }

                    toggleRandomMove(true);
                },
                Move = move,
                MoveTo = async (destination) =>
                {
                    toggleRandomMove(false);
                    var path = Path(previous, VectorToBoundingboxDefaultWidth(destination));
                    previous = await tracePath(path, false, page, previous);
                    toggleRandomMove(true);
                }
            };

            // Start random mouse movements. Do not await the promise but return immediately
            if (performRandomMoves)
                previous = await randomMove(page, previous);

            return actions;

        }
    }
}