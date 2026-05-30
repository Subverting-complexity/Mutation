using System;
using System.Threading.Tasks;
using Windows.Storage;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using CognitiveSupport;
using Windows.Graphics;

namespace Mutation.Ui.Services;

public class UiStateManager
{
private readonly Settings _settings;

public UiStateManager(Settings settings)
{
_settings = settings;
}

public void Restore(Window window)
{
if (window == null) throw new ArgumentNullException(nameof(window));
var appWindow = window.AppWindow;
var ui = _settings.MainWindowUiSettings;
if (appWindow == null || ui == null)
return;

var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
var bounds = displayArea.WorkArea;

const int minWidth = 150;
const int minHeight = 150;

// No persisted size yet (brand-new settings file): open at 75% of the work
// area, centred. Without this the window falls back to the 150x150 minimum at
// (0,0) — a tiny square in the top-left corner.
if (ui.WindowSize.Width <= 0 || ui.WindowSize.Height <= 0)
{
int defaultWidth = Math.Max(minWidth, (int)(bounds.Width * 0.75));
int defaultHeight = Math.Max(minHeight, (int)(bounds.Height * 0.75));
int defaultX = bounds.X + (bounds.Width - defaultWidth) / 2;
int defaultY = bounds.Y + (bounds.Height - defaultHeight) / 2;

appWindow.Resize(new SizeInt32(defaultWidth, defaultHeight));
appWindow.Move(new PointInt32(defaultX, defaultY));
return;
}

int width = Math.Max(minWidth, Math.Min(ui.WindowSize.Width, bounds.Width));
int height = Math.Max(minHeight, Math.Min(ui.WindowSize.Height, bounds.Height));

int x = Math.Max(bounds.X, Math.Min(ui.WindowLocation.X, bounds.X + bounds.Width - width));
int y = Math.Max(bounds.Y, Math.Min(ui.WindowLocation.Y, bounds.Y + bounds.Height - height));

appWindow.Resize(new SizeInt32(width, height));
appWindow.Move(new PointInt32(x, y));
}

public void Save(Window window)
{
if (window == null) throw new ArgumentNullException(nameof(window));
var appWindow = window.AppWindow;
_settings.MainWindowUiSettings.WindowSize = new System.Drawing.Size(appWindow.Size.Width, appWindow.Size.Height);
_settings.MainWindowUiSettings.WindowLocation = new System.Drawing.Point(appWindow.Position.X, appWindow.Position.Y);
}
}
